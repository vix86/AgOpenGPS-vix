using System;
using Tinkerforge;

namespace AgOpenGPS.Hardware.CereaStyle
{
    public sealed class TinkerforgeCereaImu : IDisposable
    {
        private readonly TinkerforgeCereaImuSettings settings;
        private IPConnection ipConnection;
        private BrickIMUV2 imu;
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
                ipConnection = new IPConnection();
                imu = new BrickIMUV2(settings.Uid, ipConnection);
                ipConnection.Connect(settings.Host, settings.Port);
                Connected = true;
                Refresh();
            }
            catch (Exception ex)
            {
                LastError = "IMU connect failed: " + ex.Message;
                Connected = false;
                TryClose();
            }
        }

        public void Refresh()
        {
            if (!Connected || imu == null) return;

            try
            {
                short heading;
                short roll;
                short pitch;
                imu.GetOrientation(out heading, out roll, out pitch);
                HeadingDegrees = NormalizeDegrees(heading / 16.0 + settings.HeadingOffsetDegrees);
                RollDegrees = roll / 16.0;
                PitchDegrees = pitch / 16.0;
            }
            catch (Exception ex)
            {
                LastError = "IMU read failed: " + ex.Message;
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
            TryClose();
        }

        private void TryClose()
        {
            try { ipConnection?.Disconnect(); } catch { }
            ipConnection = null;
            imu = null;
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
