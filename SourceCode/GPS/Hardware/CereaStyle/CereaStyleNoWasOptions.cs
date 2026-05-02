using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AgOpenGPS.Hardware.CereaStyle
{
    public sealed class CereaStyleNoWasOptions
    {
        public const string FileName = "CereaStyleNoWas.ini";

        public bool Enabled { get; private set; }
        public bool PanelMinimized { get; private set; }
        public bool AutoConnect { get; private set; }
        public bool AutoArm { get; private set; }
        public bool AutoStartWithAogAutoSteer { get; private set; }
        public CereaStyleNoWasSettings ControllerSettings { get; private set; }
        public PhidgetsCereaMotorSettings MotorSettings { get; private set; }
        public TinkerforgeCereaImuSettings ImuSettings { get; private set; }
        public string LoadedPath { get; private set; }
        public string StatusMessage { get; private set; }

        private CereaStyleNoWasOptions()
        {
            ControllerSettings = new CereaStyleNoWasSettings();
            MotorSettings = new PhidgetsCereaMotorSettings();
            ImuSettings = new TinkerforgeCereaImuSettings();
            LoadedPath = string.Empty;
            StatusMessage = string.Empty;
        }

        public static CereaStyleNoWasOptions Load()
        {
            var options = new CereaStyleNoWasOptions();
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
            options.LoadedPath = path;

            if (!File.Exists(path))
            {
                options.Enabled = false;
                options.StatusMessage = "Config file not found; Cerea No-WAS disabled.";
                return options;
            }

            var values = ReadIni(path);
            options.Enabled = GetBool(values, "bridge.enabled", false);
            options.PanelMinimized = GetBool(values, "startup.panelMinimized", true);
            options.AutoConnect = GetBool(values, "startup.autoConnect", true);
            options.AutoArm = GetBool(values, "startup.autoArm", true);
            options.AutoStartWithAogAutoSteer = GetBool(values, "startup.autoStartWithAogAutoSteer", true);

            options.ControllerSettings.RequireRtkFix = GetBool(values, "safety.requireRtkFix", true);
            options.ControllerSettings.MinimumSpeedKph = GetDouble(values, "safety.minSpeedKph", 0.4);
            options.ControllerSettings.MaximumSpeedKph = GetDouble(values, "safety.maxSpeedKph", 20.0);
            options.ControllerSettings.GpsWatchdogMilliseconds = GetInt(values, "safety.gpsWatchdogMs", 500);

            options.ControllerSettings.LookAheadMeters = GetDouble(values, "controller.lookAheadMeters", 8.0);
            options.ControllerSettings.CrossTrackGain = GetDouble(values, "controller.crossTrackGain", 0.18);
            options.ControllerSettings.HeadingGain = GetDouble(values, "controller.headingGain", 0.035);
            options.ControllerSettings.MaximumCommand = GetDouble(values, "controller.maximumCommand", 0.45);
            options.ControllerSettings.MinimumCommand = GetDouble(values, "controller.minimumCommand", 0.02);
            options.ControllerSettings.CommandRateLimitPerCycle = GetDouble(values, "controller.commandRateLimitPerCycle", 0.05);
            options.ControllerSettings.InvertGuidanceCommand = GetBool(values, "controller.invertGuidanceCommand", false);

            options.ControllerSettings.EncoderSoftLimitCounts = GetLong(values, "encoder.softLimitCounts", 18000);
            options.ControllerSettings.EncoderHardLimitCounts = GetLong(values, "encoder.hardLimitCounts", 24000);
            options.ControllerSettings.EncoderCenteringGain = GetDouble(values, "encoder.centeringGain", 0.08);
            options.ControllerSettings.EncoderMovementThresholdCounts = GetInt(values, "encoder.movementThresholdCounts", 4);
            options.ControllerSettings.StallDetectionEnabled = GetBool(values, "encoder.stallDetection", false);
            options.ControllerSettings.StallCommandThreshold = GetDouble(values, "encoder.stallCommandThreshold", 0.12);
            options.ControllerSettings.StallTimeoutMilliseconds = GetInt(values, "encoder.stallTimeoutMs", 750);

            options.MotorSettings.Driver = GetString(values, "motor.driver", "phidget22");
            options.MotorSettings.SerialNumber = GetInt(values, "motor.serial", 0);
            options.MotorSettings.MotorSerialNumber = GetInt(values, "motor.motorSerial", 0);
            options.MotorSettings.EncoderSerialNumber = GetInt(values, "motor.encoderSerial", 0);
            options.MotorSettings.MotorChannel = GetInt(values, "motor.motorChannel", 0);
            options.MotorSettings.EncoderChannel = GetInt(values, "motor.encoderChannel", 0);
            options.MotorSettings.MaximumTargetVelocity = GetDouble(values, "motor.maximumTargetVelocity", 0.35);
            options.MotorSettings.Acceleration = GetDouble(values, "motor.acceleration", 4.0);
            options.MotorSettings.InvertMotorOutput = GetBool(values, "motor.invertMotorOutput", false);
            options.MotorSettings.OpenTimeoutMilliseconds = GetInt(values, "motor.openTimeoutMs", 5000);

            options.ImuSettings.Enabled = GetBool(values, "imu.enabled", false);
            options.ImuSettings.Host = GetString(values, "imu.host", "localhost");
            options.ImuSettings.Port = GetInt(values, "imu.port", 4223);
            options.ImuSettings.Uid = GetString(values, "imu.uid", string.Empty);
            options.ImuSettings.HeadingOffsetDegrees = GetDouble(values, "imu.headingOffsetDegrees", 0.0);
            options.ImuSettings.FeedAogAhrs = GetBool(values, "imu.feedAogAhrs", true);

            options.StatusMessage = options.Enabled ? "Config enabled; Cerea No-WAS available." : "Config loaded; Cerea No-WAS disabled.";
            return options;
        }

        private static Dictionary<string, string> ReadIni(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var section = string.Empty;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();
                values[(section.Length == 0 ? key : section + "." + key)] = value;
            }
            return values;
        }

        private static string GetString(Dictionary<string, string> values, string key, string fallback)
        {
            return values.TryGetValue(key, out var value) ? value : fallback;
        }

        private static bool GetBool(Dictionary<string, string> values, string key, bool fallback)
        {
            if (!values.TryGetValue(key, out var value)) return fallback;
            if (value == "1") return true;
            if (value == "0") return false;
            return bool.TryParse(value, out var result) ? result : fallback;
        }

        private static int GetInt(Dictionary<string, string> values, string key, int fallback)
        {
            return values.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : fallback;
        }

        private static long GetLong(Dictionary<string, string> values, string key, long fallback)
        {
            return values.TryGetValue(key, out var value) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : fallback;
        }

        private static double GetDouble(Dictionary<string, string> values, string key, double fallback)
        {
            return values.TryGetValue(key, out var value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : fallback;
        }
    }
}
