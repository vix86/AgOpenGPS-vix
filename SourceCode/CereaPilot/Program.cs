using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace CereaPilot;

internal static class Program
{
    private static readonly object Sync = new();
    private static readonly PilotState State = new();
    private static PilotConfig Config = PilotConfig.Load();
    private static GpsReader? _gps;
    private static PhidgetsMotor? _motor;
    private static ImuBrickReader? _imu;
    private static Timer? _controlTimer;
    private static DateTime _lastGoodControl = DateTime.MinValue;

    private static void Main()
    {
        Console.Title = "CereaPilot - minimal safe prototype";
        PrintHeader();

        _motor = new PhidgetsMotor(Config);
        _imu = new ImuBrickReader(Config);
        _controlTimer = new Timer(ControlTick, null, Config.ControlPeriodMs, Config.ControlPeriodMs);

        RunConsoleLoop();

        SafeStopMotor();
        _controlTimer?.Dispose();
        _gps?.Dispose();
        _imu?.Dispose();
        _motor?.Dispose();
    }

    private static void PrintHeader()
    {
        Console.WriteLine("CereaPilot minimal prototype");
        Console.WriteLine("Motor output: DISABLED by default");
        Console.WriteLine("Encoder is used only as motor feedback/soft limit, never as wheel angle/WAS.");
        Console.WriteLine();
        PrintHelp();
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  ports                         - list COM ports");
        Console.WriteLine("  gps COMx [baud]               - open F9P NMEA serial port, example: gps COM5 115200");
        Console.WriteLine("  phidgets                      - connect Phidgets 1065_1B motor + encoder");
        Console.WriteLine("  imu [uid]                     - connect Tinkerforge IMU Brick 2.0");
        Console.WriteLine("  a                             - set A point from current GPS position");
        Console.WriteLine("  b                             - set B point from current GPS position");
        Console.WriteLine("  ab latA lonA latB lonB        - manually enter A/B line");
        Console.WriteLine("  arm                           - allow motor output, still stopped until start");
        Console.WriteLine("  disarm                        - disable motor output immediately");
        Console.WriteLine("  start                         - start autopilot if armed + GPS + AB are valid");
        Console.WriteLine("  stop                          - stop autopilot, keep arm state");
        Console.WriteLine("  status                        - show live status");
        Console.WriteLine("  help                          - show this help");
        Console.WriteLine("  quit                          - exit");
        Console.WriteLine();
    }

    private static void RunConsoleLoop()
    {
        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line is null)
            {
                return;
            }

            var parts = SplitCommand(line);
            if (parts.Length == 0)
            {
                continue;
            }

            try
            {
                switch (parts[0].ToLowerInvariant())
                {
                    case "ports":
                        Console.WriteLine("COM ports: " + string.Join(", ", SerialPort.GetPortNames().OrderBy(p => p)));
                        break;

                    case "gps":
                        OpenGps(parts);
                        break;

                    case "phidgets":
                        _motor?.Connect();
                        break;

                    case "imu":
                        ConnectImu(parts);
                        break;

                    case "a":
                        SetPoint(true);
                        break;

                    case "b":
                        SetPoint(false);
                        break;

                    case "ab":
                        SetManualAb(parts);
                        break;

                    case "arm":
                        lock (Sync)
                        {
                            State.MotorArmed = true;
                            State.AutopilotEnabled = false;
                        }
                        SafeStopMotor();
                        Console.WriteLine("Motor ARMED, autopilot still STOPPED.");
                        break;

                    case "disarm":
                        lock (Sync)
                        {
                            State.AutopilotEnabled = false;
                            State.MotorArmed = false;
                        }
                        SafeStopMotor();
                        Console.WriteLine("Motor DISARMED and stopped.");
                        break;

                    case "start":
                        StartAutopilot();
                        break;

                    case "stop":
                        lock (Sync)
                        {
                            State.AutopilotEnabled = false;
                        }
                        SafeStopMotor();
                        Console.WriteLine("Autopilot STOPPED.");
                        break;

                    case "status":
                        PrintStatus();
                        break;

                    case "help":
                        PrintHelp();
                        break;

                    case "quit":
                    case "exit":
                        return;

                    default:
                        Console.WriteLine("Unknown command. Type help.");
                        break;
                }
            }
            catch (Exception ex)
            {
                SafeStopMotor();
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }

    private static string[] SplitCommand(string line)
    {
        return line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static void OpenGps(string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.WriteLine("Usage: gps COMx [baud]");
            return;
        }

        var baud = parts.Length >= 3 ? int.Parse(parts[2], CultureInfo.InvariantCulture) : Config.GpsBaud;
        _gps?.Dispose();
        _gps = new GpsReader(parts[1], baud, UpdateGpsState);
        _gps.Start();
        Console.WriteLine($"GPS opened: {parts[1]} @ {baud}");
    }

    private static void ConnectImu(string[] parts)
    {
        if (parts.Length >= 2)
        {
            Config.ImuUid = parts[1];
        }

        _imu?.Connect();
    }

    private static void SetPoint(bool pointA)
    {
        GpsState gps;
        lock (Sync)
        {
            gps = State.Gps;
        }

        if (!gps.HasPosition)
        {
            Console.WriteLine("No GPS position yet.");
            return;
        }

        lock (Sync)
        {
            if (pointA)
            {
                State.PointA = new GeoPoint(gps.LatitudeDeg, gps.LongitudeDeg);
            }
            else
            {
                State.PointB = new GeoPoint(gps.LatitudeDeg, gps.LongitudeDeg);
            }
        }

        Console.WriteLine(pointA ? "A point set from GPS." : "B point set from GPS.");
    }

    private static void SetManualAb(string[] parts)
    {
        if (parts.Length != 5)
        {
            Console.WriteLine("Usage: ab latA lonA latB lonB");
            return;
        }

        var latA = double.Parse(parts[1], CultureInfo.InvariantCulture);
        var lonA = double.Parse(parts[2], CultureInfo.InvariantCulture);
        var latB = double.Parse(parts[3], CultureInfo.InvariantCulture);
        var lonB = double.Parse(parts[4], CultureInfo.InvariantCulture);

        lock (Sync)
        {
            State.PointA = new GeoPoint(latA, lonA);
            State.PointB = new GeoPoint(latB, lonB);
            State.AutopilotEnabled = false;
        }

        SafeStopMotor();
        Console.WriteLine("Manual A/B line set. Autopilot remains STOPPED.");
    }

    private static void StartAutopilot()
    {
        lock (Sync)
        {
            if (!State.MotorArmed)
            {
                Console.WriteLine("Refused: motor is DISARMED. Use arm first.");
                return;
            }

            if (!State.Gps.HasPosition)
            {
                Console.WriteLine("Refused: no GPS position.");
                return;
            }

            if (!State.HasAbLine)
            {
                Console.WriteLine("Refused: A/B line is not set.");
                return;
            }

            State.AutopilotEnabled = true;
            _lastGoodControl = DateTime.UtcNow;
        }

        Console.WriteLine("Autopilot STARTED.");
    }

    private static void UpdateGpsState(GpsState gps)
    {
        lock (Sync)
        {
            State.Gps = gps;
        }
    }

    private static void ControlTick(object? _)
    {
        try
        {
            PilotSnapshot s;
            lock (Sync)
            {
                s = State.Snapshot();
            }

            var imuHeading = _imu?.TryReadHeadingDeg();
            if (imuHeading.HasValue)
            {
                lock (Sync)
                {
                    State.ImuHeadingDeg = imuHeading.Value;
                    State.ImuConnected = true;
                }
            }

            if (!s.MotorArmed || !s.AutopilotEnabled)
            {
                SafeStopMotor();
                return;
            }

            if ((DateTime.UtcNow - s.Gps.LastUpdateUtc).TotalMilliseconds > Config.WatchdogMs)
            {
                FaultStop("GPS watchdog timeout");
                return;
            }

            if (Config.RequireRtkFix && !s.Gps.IsRtkFixed)
            {
                FaultStop("RTK FIX lost");
                return;
            }

            if (s.Gps.SpeedKph < Config.MinSpeedKph || s.Gps.SpeedKph > Config.MaxSpeedKph)
            {
                SafeStopMotor();
                return;
            }

            if (!s.HasAbLine || !s.Gps.HasPosition)
            {
                SafeStopMotor();
                return;
            }

            var xte = GeoMath.CrossTrackErrorMeters(s.PointA!.Value, s.PointB!.Value, new GeoPoint(s.Gps.LatitudeDeg, s.Gps.LongitudeDeg));
            var lineHeading = GeoMath.BearingDeg(s.PointA.Value, s.PointB.Value);
            var currentHeading = ChooseHeading(s, imuHeading);
            var headingError = GeoMath.AngleDiffDeg(lineHeading, currentHeading);

            var desired = Math.Atan2(xte, Config.LookAheadMeters) * 180.0 / Math.PI;
            var command = (Config.XteGain * desired) + (Config.HeadingGain * headingError);
            command = Math.Clamp(command, -Config.MaxCommand, Config.MaxCommand);

            var limited = _motor?.ApplyEncoderLimit(command) ?? 0.0;
            _motor?.SetVelocity(limited);

            lock (Sync)
            {
                State.LastXteMeters = xte;
                State.LastLineHeadingDeg = lineHeading;
                State.LastHeadingErrorDeg = headingError;
                State.LastMotorCommand = limited;
            }

            _lastGoodControl = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            FaultStop("Control error: " + ex.Message);
        }
    }

    private static double ChooseHeading(PilotSnapshot s, double? imuHeading)
    {
        if (s.Gps.SpeedKph >= 1.2 && s.Gps.HasCourse)
        {
            return s.Gps.CourseDeg;
        }

        if (imuHeading.HasValue)
        {
            return imuHeading.Value;
        }

        return s.Gps.CourseDeg;
    }

    private static void FaultStop(string reason)
    {
        lock (Sync)
        {
            State.AutopilotEnabled = false;
            State.MotorArmed = false;
            State.LastFault = reason;
        }

        SafeStopMotor();
        Console.WriteLine("SAFETY STOP: " + reason);
    }

    private static void SafeStopMotor()
    {
        try
        {
            _motor?.Stop();
        }
        catch
        {
            // Safety stop must never throw back into the controller loop.
        }
    }

    private static void PrintStatus()
    {
        PilotSnapshot s;
        lock (Sync)
        {
            s = State.Snapshot();
        }

        Console.WriteLine($"GPS: pos={s.Gps.HasPosition} lat={s.Gps.LatitudeDeg:F8} lon={s.Gps.LongitudeDeg:F8} fix={s.Gps.FixText} rtkFix={s.Gps.IsRtkFixed} speed={s.Gps.SpeedKph:F2} kph course={s.Gps.CourseDeg:F1}");
        Console.WriteLine($"AB: {s.HasAbLine} A={FormatPoint(s.PointA)} B={FormatPoint(s.PointB)}");
        Console.WriteLine($"Pilot: armed={s.MotorArmed} enabled={s.AutopilotEnabled} xte={s.LastXteMeters:F2} m line={s.LastLineHeadingDeg:F1} headingErr={s.LastHeadingErrorDeg:F1} cmd={s.LastMotorCommand:F3}");
        Console.WriteLine($"Motor: connected={_motor?.IsConnected == true} encoder={_motor?.EncoderPosition ?? 0} fault={s.LastFault}");
        Console.WriteLine($"IMU: connected={s.ImuConnected} heading={s.ImuHeadingDeg:F1}");
    }

    private static string FormatPoint(GeoPoint? p)
    {
        return p.HasValue ? $"{p.Value.LatitudeDeg:F8},{p.Value.LongitudeDeg:F8}" : "-";
    }
}

internal sealed class PilotConfig
{
    public string GpsPort { get; set; } = string.Empty;
    public int GpsBaud { get; set; } = 115200;
    public string ImuHost { get; set; } = "localhost";
    public int ImuPort { get; set; } = 4223;
    public string ImuUid { get; set; } = string.Empty;
    public double LookAheadMeters { get; set; } = 8.0;
    public double XteGain { get; set; } = 0.18;
    public double HeadingGain { get; set; } = 0.035;
    public double MaxCommand { get; set; } = 0.45;
    public double MinSpeedKph { get; set; } = 0.4;
    public double MaxSpeedKph { get; set; } = 20.0;
    public bool RequireRtkFix { get; set; } = true;
    public int ControlPeriodMs { get; set; } = 50;
    public int WatchdogMs { get; set; } = 300;
    public double MaxVelocity { get; set; } = 0.35;
    public long EncoderSoftLimitCounts { get; set; } = 18000;
    public long EncoderHomeToleranceCounts { get; set; } = 200;
    public bool InvertMotor { get; set; }

    public static PilotConfig Load()
    {
        var cfg = new PilotConfig();
        var path = Path.Combine(AppContext.BaseDirectory, "CereaPilot.profile.ini");
        if (!File.Exists(path))
        {
            path = Path.Combine(Directory.GetCurrentDirectory(), "CereaPilot.profile.ini");
        }

        if (!File.Exists(path))
        {
            return cfg;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('['))
            {
                continue;
            }

            var idx = line.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            values[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }

        cfg.GpsPort = Get(values, "port", cfg.GpsPort);
        cfg.GpsBaud = GetInt(values, "baud", cfg.GpsBaud);
        cfg.ImuHost = Get(values, "host", cfg.ImuHost);
        cfg.ImuPort = GetInt(values, "port", cfg.ImuPort);
        cfg.ImuUid = Get(values, "uid", cfg.ImuUid);
        cfg.LookAheadMeters = GetDouble(values, "lookAheadMeters", cfg.LookAheadMeters);
        cfg.XteGain = GetDouble(values, "xteGain", cfg.XteGain);
        cfg.HeadingGain = GetDouble(values, "headingGain", cfg.HeadingGain);
        cfg.MaxCommand = GetDouble(values, "maxCommand", cfg.MaxCommand);
        cfg.MinSpeedKph = GetDouble(values, "minSpeedKph", cfg.MinSpeedKph);
        cfg.MaxSpeedKph = GetDouble(values, "maxSpeedKph", cfg.MaxSpeedKph);
        cfg.RequireRtkFix = GetBool(values, "requireRtkFix", cfg.RequireRtkFix);
        cfg.ControlPeriodMs = GetInt(values, "controlPeriodMs", cfg.ControlPeriodMs);
        cfg.WatchdogMs = GetInt(values, "watchdogMs", cfg.WatchdogMs);
        cfg.MaxVelocity = GetDouble(values, "maxVelocity", cfg.MaxVelocity);
        cfg.EncoderSoftLimitCounts = GetLong(values, "encoderSoftLimitCounts", cfg.EncoderSoftLimitCounts);
        cfg.EncoderHomeToleranceCounts = GetLong(values, "encoderHomeToleranceCounts", cfg.EncoderHomeToleranceCounts);
        cfg.InvertMotor = GetBool(values, "invertMotor", cfg.InvertMotor);
        return cfg;
    }

    private static string Get(Dictionary<string, string> values, string key, string fallback) => values.TryGetValue(key, out var v) ? v : fallback;
    private static int GetInt(Dictionary<string, string> values, string key, int fallback) => values.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : fallback;
    private static long GetLong(Dictionary<string, string> values, string key, long fallback) => values.TryGetValue(key, out var v) && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : fallback;
    private static double GetDouble(Dictionary<string, string> values, string key, double fallback) => values.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : fallback;
    private static bool GetBool(Dictionary<string, string> values, string key, bool fallback) => values.TryGetValue(key, out var v) && bool.TryParse(v, out var r) ? r : fallback;
}

internal sealed class PilotState
{
    public GpsState Gps { get; set; } = new();
    public GeoPoint? PointA { get; set; }
    public GeoPoint? PointB { get; set; }
    public bool MotorArmed { get; set; }
    public bool AutopilotEnabled { get; set; }
    public bool ImuConnected { get; set; }
    public double ImuHeadingDeg { get; set; }
    public double LastXteMeters { get; set; }
    public double LastLineHeadingDeg { get; set; }
    public double LastHeadingErrorDeg { get; set; }
    public double LastMotorCommand { get; set; }
    public string LastFault { get; set; } = string.Empty;
    public bool HasAbLine => PointA.HasValue && PointB.HasValue && GeoMath.DistanceMeters(PointA.Value, PointB.Value) > 0.5;

    public PilotSnapshot Snapshot() => new(Gps, PointA, PointB, HasAbLine, MotorArmed, AutopilotEnabled, ImuConnected, ImuHeadingDeg, LastXteMeters, LastLineHeadingDeg, LastHeadingErrorDeg, LastMotorCommand, LastFault);
}

internal sealed record PilotSnapshot(GpsState Gps, GeoPoint? PointA, GeoPoint? PointB, bool HasAbLine, bool MotorArmed, bool AutopilotEnabled, bool ImuConnected, double ImuHeadingDeg, double LastXteMeters, double LastLineHeadingDeg, double LastHeadingErrorDeg, double LastMotorCommand, string LastFault);
internal readonly record struct GeoPoint(double LatitudeDeg, double LongitudeDeg);

internal sealed class GpsState
{
    public bool HasPosition { get; set; }
    public double LatitudeDeg { get; set; }
    public double LongitudeDeg { get; set; }
    public double SpeedKph { get; set; }
    public bool HasCourse { get; set; }
    public double CourseDeg { get; set; }
    public string FixText { get; set; } = "none";
    public bool IsRtkFixed { get; set; }
    public DateTime LastUpdateUtc { get; set; } = DateTime.MinValue;
}

internal sealed class GpsReader : IDisposable
{
    private readonly SerialPort _port;
    private readonly Action<GpsState> _onUpdate;
    private readonly GpsState _state = new();

    public GpsReader(string portName, int baud, Action<GpsState> onUpdate)
    {
        _onUpdate = onUpdate;
        _port = new SerialPort(portName, baud)
        {
            NewLine = "\n",
            ReadTimeout = 500,
            DtrEnable = true,
            RtsEnable = true
        };
        _port.DataReceived += OnDataReceived;
    }

    public void Start() => _port.Open();

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        while (_port.IsOpen && _port.BytesToRead > 0)
        {
            var line = _port.ReadLine().Trim();
            if (NmeaParser.TryApply(line, _state))
            {
                _state.LastUpdateUtc = DateTime.UtcNow;
                _onUpdate(Clone(_state));
            }
        }
    }

    private static GpsState Clone(GpsState s) => new()
    {
        HasPosition = s.HasPosition,
        LatitudeDeg = s.LatitudeDeg,
        LongitudeDeg = s.LongitudeDeg,
        SpeedKph = s.SpeedKph,
        HasCourse = s.HasCourse,
        CourseDeg = s.CourseDeg,
        FixText = s.FixText,
        IsRtkFixed = s.IsRtkFixed,
        LastUpdateUtc = s.LastUpdateUtc
    };

    public void Dispose()
    {
        try { _port.Close(); } catch { }
        _port.Dispose();
    }
}

internal static class NmeaParser
{
    public static bool TryApply(string line, GpsState state)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith('$'))
        {
            return false;
        }

        if (!ChecksumOk(line))
        {
            return false;
        }

        var body = line.Split('*')[0];
        var f = body.Split(',');
        var type = f[0].Length >= 6 ? f[0][3..] : f[0].TrimStart('$');

        if (type.Equals("GGA", StringComparison.OrdinalIgnoreCase) && f.Length > 9)
        {
            if (TryParseLatLon(f[2], f[3], f[4], f[5], out var lat, out var lon))
            {
                state.LatitudeDeg = lat;
                state.LongitudeDeg = lon;
                state.HasPosition = true;
            }

            state.FixText = f[6] switch
            {
                "0" => "invalid",
                "1" => "gps",
                "2" => "dgps",
                "4" => "rtk-fix",
                "5" => "rtk-float",
                _ => f[6]
            };
            state.IsRtkFixed = f[6] == "4";
            return true;
        }

        if (type.Equals("RMC", StringComparison.OrdinalIgnoreCase) && f.Length > 8)
        {
            if (f[2].Equals("A", StringComparison.OrdinalIgnoreCase) && TryParseLatLon(f[3], f[4], f[5], f[6], out var lat, out var lon))
            {
                state.LatitudeDeg = lat;
                state.LongitudeDeg = lon;
                state.HasPosition = true;
            }

            if (double.TryParse(f[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var knots))
            {
                state.SpeedKph = knots * 1.852;
            }

            if (double.TryParse(f[8], NumberStyles.Float, CultureInfo.InvariantCulture, out var course))
            {
                state.CourseDeg = GeoMath.NormalizeDeg(course);
                state.HasCourse = true;
            }

            return true;
        }

        if (type.Equals("VTG", StringComparison.OrdinalIgnoreCase) && f.Length > 7)
        {
            if (double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var course))
            {
                state.CourseDeg = GeoMath.NormalizeDeg(course);
                state.HasCourse = true;
            }

            if (double.TryParse(f[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var kph))
            {
                state.SpeedKph = kph;
            }

            return true;
        }

        return false;
    }

    private static bool TryParseLatLon(string latRaw, string ns, string lonRaw, string ew, out double lat, out double lon)
    {
        lat = 0;
        lon = 0;
        if (!TryParseNmeaCoordinate(latRaw, true, out lat) || !TryParseNmeaCoordinate(lonRaw, false, out lon))
        {
            return false;
        }

        if (ns.Equals("S", StringComparison.OrdinalIgnoreCase)) lat = -lat;
        if (ew.Equals("W", StringComparison.OrdinalIgnoreCase)) lon = -lon;
        return true;
    }

    private static bool TryParseNmeaCoordinate(string raw, bool isLat, out double deg)
    {
        deg = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var degreeDigits = isLat ? 2 : 3;
        if (raw.Length <= degreeDigits)
        {
            return false;
        }

        if (!double.TryParse(raw[..degreeDigits], NumberStyles.Integer, CultureInfo.InvariantCulture, out var wholeDeg))
        {
            return false;
        }

        if (!double.TryParse(raw[degreeDigits..], NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes))
        {
            return false;
        }

        deg = wholeDeg + (minutes / 60.0);
        return true;
    }

    private static bool ChecksumOk(string line)
    {
        var star = line.IndexOf('*');
        if (star < 0)
        {
            return true;
        }

        if (star + 2 >= line.Length)
        {
            return false;
        }

        var calc = 0;
        for (var i = 1; i < star; i++)
        {
            calc ^= line[i];
        }

        return int.TryParse(line.Substring(star + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var sent) && calc == sent;
    }
}

internal sealed class PhidgetsMotor : IDisposable
{
    private readonly PilotConfig _cfg;
    private object? _motor;
    private object? _encoder;
    private readonly Type? _dcMotorType;
    private readonly Type? _encoderType;

    public PhidgetsMotor(PilotConfig cfg)
    {
        _cfg = cfg;
        _dcMotorType = FindType("Phidget22.DCMotor");
        _encoderType = FindType("Phidget22.Encoder");
    }

    public bool IsConnected => GetBoolProperty(_motor, "Attached") || _motor is not null;
    public long EncoderPosition => Convert.ToInt64(GetProperty(_encoder, "Position") ?? 0, CultureInfo.InvariantCulture);

    public void Connect()
    {
        if (_dcMotorType is null)
        {
            Console.WriteLine("Phidget22.DCMotor type not found. Check Phidget22.NET package/runtime.");
            return;
        }

        _motor ??= Activator.CreateInstance(_dcMotorType);
        TryInvoke(_motor, "Open", 5000);
        TrySetProperty(_motor, "TargetVelocity", 0.0);
        TrySetProperty(_motor, "Acceleration", 4.0);

        if (_encoderType is not null)
        {
            _encoder ??= Activator.CreateInstance(_encoderType);
            TryInvoke(_encoder, "Open", 5000);
        }

        Console.WriteLine($"Phidgets connected. Motor={_motor is not null}, Encoder={_encoder is not null}, EncoderPosition={EncoderPosition}");
    }

    public double ApplyEncoderLimit(double command)
    {
        var pos = EncoderPosition;
        if (Math.Abs(pos) <= _cfg.EncoderSoftLimitCounts)
        {
            return command;
        }

        if (pos > _cfg.EncoderSoftLimitCounts && command > 0)
        {
            return 0;
        }

        if (pos < -_cfg.EncoderSoftLimitCounts && command < 0)
        {
            return 0;
        }

        return command;
    }

    public void SetVelocity(double command)
    {
        if (_motor is null)
        {
            return;
        }

        var velocity = Math.Clamp(command, -1.0, 1.0) * _cfg.MaxVelocity;
        if (_cfg.InvertMotor)
        {
            velocity = -velocity;
        }

        TrySetProperty(_motor, "TargetVelocity", velocity);
    }

    public void Stop()
    {
        if (_motor is null)
        {
            return;
        }

        TrySetProperty(_motor, "TargetVelocity", 0.0);
    }

    public void Dispose()
    {
        Stop();
        TryInvoke(_encoder, "Close");
        TryInvoke(_motor, "Close");
    }

    private static Type? FindType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = asm.GetType(fullName, false);
            if (type is not null)
            {
                return type;
            }
        }

        try
        {
            var asm = Assembly.Load("Phidget22.NET");
            return asm.GetType(fullName, false);
        }
        catch
        {
            return null;
        }
    }

    private static object? GetProperty(object? target, string name) => target?.GetType().GetProperty(name)?.GetValue(target);
    private static bool GetBoolProperty(object? target, string name) => GetProperty(target, name) is bool b && b;
    private static void TrySetProperty(object? target, string name, object value)
    {
        var prop = target?.GetType().GetProperty(name);
        if (prop is null || !prop.CanWrite)
        {
            return;
        }

        var converted = Convert.ChangeType(value, prop.PropertyType, CultureInfo.InvariantCulture);
        prop.SetValue(target, converted);
    }

    private static void TryInvoke(object? target, string name, params object[] args)
    {
        if (target is null)
        {
            return;
        }

        var method = target.GetType().GetMethods().FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length);
        method?.Invoke(target, args);
    }
}

internal sealed class ImuBrickReader : IDisposable
{
    private readonly PilotConfig _cfg;
    private object? _ipcon;
    private object? _imu;
    private Type? _ipConnectionType;
    private Type? _brickType;

    public ImuBrickReader(PilotConfig cfg)
    {
        _cfg = cfg;
    }

    public void Connect()
    {
        if (string.IsNullOrWhiteSpace(_cfg.ImuUid))
        {
            Console.WriteLine("IMU UID is empty. Use: imu UID");
            return;
        }

        _ipConnectionType = FindType("Tinkerforge.IPConnection");
        _brickType = FindType("Tinkerforge.BrickIMUV2");
        if (_ipConnectionType is null || _brickType is null)
        {
            Console.WriteLine("Tinkerforge types not found. Check Tinkerforge package/runtime.");
            return;
        }

        _ipcon = Activator.CreateInstance(_ipConnectionType);
        _imu = Activator.CreateInstance(_brickType, _cfg.ImuUid, _ipcon);
        Invoke(_ipcon, "Connect", _cfg.ImuHost, _cfg.ImuPort);
        Console.WriteLine($"IMU Brick connected: {_cfg.ImuHost}:{_cfg.ImuPort}, uid={_cfg.ImuUid}");
    }

    public double? TryReadHeadingDeg()
    {
        if (_imu is null)
        {
            return null;
        }

        var method = _imu.GetType().GetMethod("GetOrientation");
        if (method is null)
        {
            return null;
        }

        var args = new object[] { 0, 0, 0 };
        method.Invoke(_imu, args);
        var headingRaw = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
        return GeoMath.NormalizeDeg(headingRaw / 16.0);
    }

    public void Dispose()
    {
        try { Invoke(_ipcon, "Disconnect"); } catch { }
    }

    private static Type? FindType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = asm.GetType(fullName, false);
            if (type is not null)
            {
                return type;
            }
        }

        try
        {
            var asm = Assembly.Load("Tinkerforge");
            return asm.GetType(fullName, false);
        }
        catch
        {
            return null;
        }
    }

    private static void Invoke(object? target, string name, params object[] args)
    {
        if (target is null)
        {
            return;
        }

        var method = target.GetType().GetMethods().FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length);
        method?.Invoke(target, args);
    }
}

internal static class GeoMath
{
    private const double EarthRadiusMeters = 6378137.0;

    public static double DistanceMeters(GeoPoint a, GeoPoint b)
    {
        ToLocalMeters(a, a, out var ax, out var ay);
        ToLocalMeters(a, b, out var bx, out var by);
        return Math.Sqrt(((bx - ax) * (bx - ax)) + ((by - ay) * (by - ay)));
    }

    public static double BearingDeg(GeoPoint a, GeoPoint b)
    {
        ToLocalMeters(a, b, out var x, out var y);
        return NormalizeDeg(Math.Atan2(x, y) * 180.0 / Math.PI);
    }

    public static double CrossTrackErrorMeters(GeoPoint a, GeoPoint b, GeoPoint p)
    {
        ToLocalMeters(a, b, out var bx, out var by);
        ToLocalMeters(a, p, out var px, out var py);
        var len = Math.Sqrt((bx * bx) + (by * by));
        if (len < 0.001)
        {
            return 0;
        }

        return ((bx * py) - (by * px)) / len;
    }

    public static double AngleDiffDeg(double target, double current)
    {
        var d = NormalizeDeg(target - current);
        return d > 180 ? d - 360 : d;
    }

    public static double NormalizeDeg(double deg)
    {
        deg %= 360.0;
        if (deg < 0)
        {
            deg += 360.0;
        }

        return deg;
    }

    private static void ToLocalMeters(GeoPoint origin, GeoPoint p, out double x, out double y)
    {
        var lat0 = origin.LatitudeDeg * Math.PI / 180.0;
        var dLat = (p.LatitudeDeg - origin.LatitudeDeg) * Math.PI / 180.0;
        var dLon = (p.LongitudeDeg - origin.LongitudeDeg) * Math.PI / 180.0;
        x = dLon * Math.Cos(lat0) * EarthRadiusMeters;
        y = dLat * EarthRadiusMeters;
    }
}
