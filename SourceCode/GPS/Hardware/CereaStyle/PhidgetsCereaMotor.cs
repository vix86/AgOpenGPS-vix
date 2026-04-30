using System;
using Phidget22;

namespace AgOpenGPS.Hardware.CereaStyle
{
    /// <summary>
    /// Phidgets 1065_1B DCMotor + Phidgets encoder adapter.
    /// Encoder position is only used as motor/steering-wheel travel feedback and safety limit.
    /// </summary>
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
            try
            {
                motor = new DCMotor();
                motor.Open(settings.OpenTimeoutMilliseconds);
                motor.Acceleration = settings.Acceleration;
                motor.TargetVelocity = 0.0;
                MotorConnected = true;
            }
            catch (Exception ex)
            {
                LastError = "Motor connect failed: " + ex.Message;
                MotorConnected = false;
            }

            try
            {
                encoder = new Encoder();
                encoder.PositionChange += OnEncoderPositionChange;
                encoder.Open(settings.OpenTimeoutMilliseconds);
                EncoderCounts = encoder.Position;
                EncoderConnected = true;
            }
            catch (Exception ex)
            {
                LastError = AppendError(LastError, "Encoder connect failed: " + ex.Message);
                EncoderConnected = false;
            }
        }

        public void ApplyCommand(double normalizedCommand)
        {
            if (!MotorConnected || motor == null)
            {
                LastTargetVelocity = 0.0;
                return;
            }

            var command = Clamp(normalizedCommand, -1.0, 1.0);
            if (settings.InvertMotorOutput)
            {
                command = -command;
            }

            var velocity = command * settings.MaximumTargetVelocity;
            try
            {
                motor.TargetVelocity = velocity;
                LastTargetVelocity = velocity;
            }
            catch (Exception ex)
            {
                LastError = "Motor command failed: " + ex.Message;
                Stop();
            }
        }

        public void Stop()
        {
            LastTargetVelocity = 0.0;
            try
            {
                if (motor != null)
                {
                    motor.TargetVelocity = 0.0;
                }
            }
            catch
            {
                // Safety stop must not throw.
            }
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

        private void OnEncoderPositionChange(object sender, EncoderPositionChangeEventArgs e)
        {
            try
            {
                if (encoder != null)
                {
                    EncoderCounts = encoder.Position;
                }
            }
            catch
            {
                EncoderCounts += e.PositionChange;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Stop();

            try
            {
                if (encoder != null)
                {
                    encoder.PositionChange -= OnEncoderPositionChange;
                    encoder.Close();
                    encoder.Dispose();
                }
            }
            catch { }

            try
            {
                if (motor != null)
                {
                    motor.Close();
                    motor.Dispose();
                }
            }
            catch { }
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
        public int OpenTimeoutMilliseconds { get; set; } = 5000;
        public double MaximumTargetVelocity { get; set; } = 0.35;
        public double Acceleration { get; set; } = 4.0;
        public bool InvertMotorOutput { get; set; }
    }
}
