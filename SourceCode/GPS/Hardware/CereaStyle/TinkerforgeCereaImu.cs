using System;
using System.Linq;
using System.Reflection;

namespace AgOpenGPS.Hardware.CereaStyle
{
    public sealed class TinkerforgeCereaImu : IDisposable
    {
        private readonly TinkerforgeCereaImuSettings settings;
        private object ipConnection;
        private object imu;
        private bool disposed;

        public TinkerforgeCereaImu(TinkerforgeCereaImuSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public bool Connected { get; private set; }
        public double HeadingDegrees { get; private set; } = 99999.0;
        public double RollDegrees { get; private set; } = 88888.0;
        public double PitchDegrees { get; private set; }
        public string LastError { get; private set; } = string.Empty;

        public void Connect()
        {
            LastError = string.Empty;
            Connected = false;

            if (!settings.Enabled)
            {
                LastError = "IMU disabled in config.";
                return;
            }

            if (string.IsNullOrWhiteSpace(settings.Uid))
            {
                LastError = "IMU UID is empty.";
                return;
            }

            try
            {
                var ipType = FindType("Tinkerforge.IPConnection");
                var imuType = FindType("Tinkerforge.BrickIMUV2");
                if (ipType == null || imuType == null)
                {
                    LastError = "Tinkerforge runtime not found.";
                    return;
                }

                ipConnection = Activator.CreateInstance(ipType);
                imu = Activator.CreateInstance(imuType, settings.Uid, ipConnection);
                Invoke(ipConnection, "Connect", settings.Host, settings.Port);
                Connected = true;
                Refresh();
            }
            catch (Exception ex)
            {
                LastError = "IMU connect failed: " + Unwrap(ex).Message;
                Connected = false;
            }
        }

        public void Refresh()
        {
            if (!Connected || imu == null) return;

            try
            {
                var method = imu.GetType().GetMethods().FirstOrDefault(m => m.Name == "GetOrientation" && m.GetParameters().Length == 3);
                if (method == null)
                {
                    LastError = "GetOrientation method not found.";
                    return;
                }

                object[] args = { 0, 0, 0 };
                method.Invoke(imu, args);
                HeadingDegrees = NormalizeDegrees(Convert.ToInt32(args[0]) / 16.0 + settings.HeadingOffsetDegrees);
                RollDegrees = Convert.ToInt32(args[1]) / 16.0;
                PitchDegrees = Convert.ToInt32(args[2]) / 16.0;
            }
            catch (Exception ex)
            {
                LastError = "IMU read failed: " + Unwrap(ex).Message;
                Connected = false;
            }
        }

        public string GetStatusText()
        {
            return "imu=" + Connected + " head=" + HeadingDegrees.ToString("0.0") + " roll=" + RollDegrees.ToString("0.0") + (string.IsNullOrWhiteSpace(LastError) ? string.Empty : " imuError=" + LastError);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            TryInvoke(ipConnection, "Disconnect");
            TryDispose(imu);
            TryDispose(ipConnection);
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
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

        private static void Invoke(object target, string methodName, params object[] args)
        {
            var method = target.GetType().GetMethods().FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == args.Length);
            if (method == null) throw new MissingMethodException(target.GetType().FullName, methodName);
            method.Invoke(target, args);
        }

        private static void TryInvoke(object target, string methodName)
        {
            try
            {
                if (target != null) Invoke(target, methodName);
            }
            catch { }
        }

        private static void TryDispose(object target)
        {
            try
            {
                var disposable = target as IDisposable;
                if (disposable != null) disposable.Dispose();
            }
            catch { }
        }

        private static Exception Unwrap(Exception ex)
        {
            var tie = ex as TargetInvocationException;
            return tie != null && tie.InnerException != null ? tie.InnerException : ex;
        }

        private static double NormalizeDegrees(double degrees)
        {
            degrees %= 360.0;
            if (degrees < 0.0) degrees += 360.0;
            return degrees;
        }
    }

    public sealed class TinkerforgeCereaImuSettings
    {
        public bool Enabled { get; set; }
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 4223;
        public string Uid { get; set; } = string.Empty;
        public double HeadingOffsetDegrees { get; set; }
        public bool FeedAogAhrs { get; set; } = true;
    }
}
