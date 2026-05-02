using System;
using Phidget22;

namespace AgOpenGPS.Hardware.CereaStyle
{
    public sealed class PhidgetsCereaMotor : IDisposable
    {
        private readonly PhidgetsCereaMotorSettings settings;
        private DCMotor motor;
        private Encoder encoder;
        private bool disposed;

        public PhidgetsCereaMotor(PhidgetsCereaMotorSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public bool MotorConnected { get; private set; }
        public bool EncoderConnected { get; private set; }
        public long EncoderCounts { get; private set; }
        public double LastTargetVelocity { get; private set; }
        public string LastError { get; private set; } = string.Empty;

        public void Connect()
        {
            LastError = string.Empty;
            ConnectMotor22();
            ConnectEncoder22();
        }

        private void ConnectMotor22()
        {
            try
            {
                motor = new DCMotor();
                var serial = settings.MotorSerialNumber > 0 ? settings.MotorSerialNumber : settings.SerialNumber;
                if (serial > 0) motor.DeviceSerialNumber = serial;
                motor.Channel = settings.MotorChannel;
                motor.Open(settings.OpenTimeoutMilliseconds);
                motor.Acceleration = settings.Acceleration;
                motor.TargetVelocity = 0.0;
                MotorConnected = true;
                LastError = AppendError(LastError, "Using Phidget22.DCMotor serial=" + (serial > 0 ? serial.ToString() : "any") + " ch=" + settings.MotorChannel + ".");
            }
            catch (Exception ex)
            {
                MotorConnected = false;
                LastError = AppendError(LastError, "Phidget22 DCMotor connect failed: " + ex.Message);
                TryCloseMotor();
            }
        }

        private void ConnectEncoder22()
        {
            try
            {
                encoder = new Encoder();
                var serial = settings.EncoderSerialNumber > 0 ? settings.EncoderSerialNumber : settings.SerialNumber;
                if (serial > 0) encoder.DeviceSerialNumber = serial;
                encoder.Channel = settings.EncoderChannel;
                encoder.Open(settings.OpenTimeoutMilliseconds);
                EncoderCounts = encoder.Position;
                EncoderConnected = true;
                LastError = AppendError(LastError, "Using Phidget22.Encoder serial=" + (serial > 0 ? serial.ToString() : "any") + " ch=" + settings.EncoderChannel + ".");
            }
            catch (Exception ex)
            {
                EncoderConnected = false;
                LastError = AppendError(LastError, "Phidget22 Encoder connect failed: " + ex.Message);
                TryCloseEncoder();
            }
        }

        public void ApplyCommand(double normalizedCommand)
        {
            if (!MotorConnected || motor == null)
            {
                LastTargetVelocity = 0.0;
                return;
            }

            RefreshEncoderPosition();
            var command = Clamp(normalizedCommand, -1.0, 1.0);
            if (settings.InvertMotorOutput) command = -command;
            var velocity = command * settings.MaximumTargetVelocity;

            try
            {
                motor.TargetVelocity = velocity;
                LastTargetVelocity = velocity;
            }
            catch (Exception ex)
            {
                LastError = "Motor command failed: " + ex.Message;
                MotorConnected = false;
                Stop();
            }
        }

        public void Stop()
        {
            LastTargetVelocity = 0.0;
            try
            {
                if (motor != null) motor.TargetVelocity = 0.0;
            }
            catch { }
        }

        public void ResetEncoderZero()
        {
            if (!EncoderConnected || encoder == null) return;
            try
            {
                encoder.Position = 0;
                EncoderCounts = 0;
            }
            catch (Exception ex)
            {
                LastError = "Encoder zero failed: " + ex.Message;
            }
        }

        public void RefreshEncoderPosition()
        {
            if (!EncoderConnected || encoder == null) return;
            try
            {
                EncoderCounts = encoder.Position;
            }
            catch (Exception ex)
            {
                EncoderConnected = false;
                LastError = "Encoder read failed: " + ex.Message;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Stop();
            TryCloseEncoder();
            TryCloseMotor();
        }

        private void TryCloseMotor()
        {
            try { motor?.Close(); } catch { }
            try { motor?.Dispose(); } catch { }
            motor = null;
        }

        private void TryCloseEncoder()
        {
            try { encoder?.Close(); } catch { }
            try { encoder?.Dispose(); } catch { }
            encoder = null;
        }

        private static string AppendError(string oldError, string newError)
        {
            return string.IsNullOrWhiteSpace(oldError) ? newError : oldError + " | " + newError;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }

    public sealed class PhidgetsCereaMotorSettings
    {
        public string Driver { get; set; } = "phidget22";
        public int SerialNumber { get; set; }
        public int MotorSerialNumber { get; set; }
        public int EncoderSerialNumber { get; set; }
        public int MotorChannel { get; set; }
        public int EncoderChannel { get; set; }
        public int OpenTimeoutMilliseconds { get; set; } = 5000;
        public double MaximumTargetVelocity { get; set; } = 0.35;
        public double Acceleration { get; set; } = 4.0;
        public bool InvertMotorOutput { get; set; }
    }
}
