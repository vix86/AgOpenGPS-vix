using System;
using System.Linq;
using System.Reflection;

namespace AgOpenGPS.Hardware.CereaStyle
{
    /// <summary>
    /// Reflection-based Phidgets 1065_1B DCMotor + Phidgets encoder adapter.
    /// This keeps the main AOG net48 project buildable even when Phidget22.NET is not referenced at compile time.
    /// Encoder position is motor/steering-wheel travel feedback only, not wheel angle/WAS.
    /// </summary>
    public sealed class PhidgetsCereaMotor : IDisposable
    {
        private readonly PhidgetsCereaMotorSettings settings;
        private object motor;
        private object encoder;
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
            ConnectMotor();
            ConnectEncoder();
        }

        private void ConnectMotor()
        {
            try
            {
                var type = FindType("Phidget22.DCMotor");
                if (type == null)
                {
                    MotorConnected = false;
                    LastError = AppendError(LastError, "Phidget22.DCMotor not found. Install Phidgets driver/runtime or copy Phidget22.NET beside AgOpenGPS.exe.");
                    return;
                }

                motor = Activator.CreateInstance(type);
                Invoke(motor, "Open", settings.OpenTimeoutMilliseconds);
                SetProperty(motor, "Acceleration", settings.Acceleration);
                SetProperty(motor, "TargetVelocity", 0.0);
                MotorConnected = true;
            }
            catch (Exception ex)
            {
                LastError = AppendError(LastError, "Motor connect failed: " + Unwrap(ex).Message);
                MotorConnected = false;
            }
        }

        private void ConnectEncoder()
        {
            try
            {
                var type = FindType("Phidget22.Encoder");
                if (type == null)
                {
                    EncoderConnected = false;
                    LastError = AppendError(LastError, "Phidget22.Encoder not found.");
                    return;
                }

                encoder = Activator.CreateInstance(type);
                Invoke(encoder, "Open", settings.OpenTimeoutMilliseconds);
                EncoderCounts = GetLongProperty(encoder, "Position");
                EncoderConnected = true;
            }
            catch (Exception ex)
            {
                LastError = AppendError(LastError, "Encoder connect failed: " + Unwrap(ex).Message);
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

            RefreshEncoderPosition();

            var command = Clamp(normalizedCommand, -1.0, 1.0);
            if (settings.InvertMotorOutput) command = -command;

            var velocity = command * settings.MaximumTargetVelocity;
            try
            {
                SetProperty(motor, "TargetVelocity", velocity);
                LastTargetVelocity = velocity;
            }
            catch (Exception ex)
            {
                LastError = "Motor command failed: " + Unwrap(ex).Message;
                Stop();
            }
        }

        public void Stop()
        {
            LastTargetVelocity = 0.0;
            try
            {
                if (motor != null) SetProperty(motor, "TargetVelocity", 0.0);
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
                SetProperty(encoder, "Position", 0);
                EncoderCounts = 0;
            }
            catch (Exception ex)
            {
                LastError = "Encoder zero failed: " + Unwrap(ex).Message;
            }
        }

        public void RefreshEncoderPosition()
        {
            if (!EncoderConnected || encoder == null) return;
            try
            {
                EncoderCounts = GetLongProperty(encoder, "Position");
            }
            catch
            {
                EncoderConnected = false;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Stop();
            TryInvoke(encoder, "Close");
            TryDispose(encoder);
            TryInvoke(motor, "Close");
            TryDispose(motor);
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }

            foreach (var name in new[] { "Phidget22.NET", "Phidget22" })
            {
                try
                {
                    var asm = Assembly.Load(name);
                    var t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { }
            }

            return null;
        }

        private static void SetProperty(object target, string name, object value)
        {
            var prop = target.GetType().GetProperty(name);
            if (prop == null || !prop.CanWrite) return;
            var converted = Convert.ChangeType(value, prop.PropertyType);
            prop.SetValue(target, converted, null);
        }

        private static long GetLongProperty(object target, string name)
        {
            var prop = target.GetType().GetProperty(name);
            if (prop == null) return 0;
            return Convert.ToInt64(prop.GetValue(target, null));
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
