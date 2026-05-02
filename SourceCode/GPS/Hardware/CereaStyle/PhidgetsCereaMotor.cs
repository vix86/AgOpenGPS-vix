using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AgOpenGPS.Hardware.CereaStyle
{
    public sealed class PhidgetsCereaMotor : IDisposable
    {
        private readonly PhidgetsCereaMotorSettings settings;
        private object motor;
        private object encoder;
        private object motorControl21;
        private bool isPhidget21;
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
            if (!isPhidget21)
            {
                ConnectEncoder();
            }
        }

        private void ConnectMotor()
        {
            var driver = (settings.Driver ?? "phidget21").Trim().ToLowerInvariant();
            if (driver == "phidget21" || driver == "1065" || driver == "1065_1b")
            {
                ConnectMotorControl21();
                return;
            }

            ConnectMotor22();
            if (!MotorConnected)
            {
                ConnectMotorControl21();
            }
        }

        private void ConnectMotor22()
        {
            var type22 = FindType("Phidget22.DCMotor");
            if (type22 == null)
            {
                LastError = AppendError(LastError, "Phidget22.DCMotor not found.");
                MotorConnected = false;
                return;
            }

            try
            {
                motor = Activator.CreateInstance(type22);
                if (settings.SerialNumber > 0) SetProperty(motor, "DeviceSerialNumber", settings.SerialNumber);
                Invoke(motor, "Open", settings.OpenTimeoutMilliseconds);
                SetProperty(motor, "Acceleration", settings.Acceleration);
                SetProperty(motor, "TargetVelocity", 0.0);
                MotorConnected = true;
                isPhidget21 = false;
            }
            catch (Exception ex)
            {
                LastError = AppendError(LastError, "Phidget22 motor failed: " + Unwrap(ex).Message);
                MotorConnected = false;
            }
        }

        private void ConnectMotorControl21()
        {
            try
            {
                var type21 = FindType("Phidgets.Devices.MotorControl") ?? FindType("Phidgets.MotorControl");
                if (type21 == null)
                {
                    MotorConnected = false;
                    EncoderConnected = false;
                    LastError = AppendError(LastError, "Phidget21 MotorControl type not found. Tried Phidgets.Devices.MotorControl and Phidgets.MotorControl.");
                    return;
                }

                motorControl21 = Activator.CreateInstance(type21);
                if (settings.SerialNumber > 0) InvokeAny(motorControl21, "open", settings.SerialNumber);
                else InvokeAny(motorControl21, "open");
                InvokeAny(motorControl21, "waitForAttachment", settings.OpenTimeoutMilliseconds);
                SetPhidget21Velocity(0.0);
                EncoderCounts = GetPhidget21EncoderPosition();
                MotorConnected = true;
                EncoderConnected = true;
                isPhidget21 = true;
                LastError = AppendError(LastError, "Using " + type21.FullName + " 1065_1B serial=" + (settings.SerialNumber > 0 ? settings.SerialNumber.ToString() : "any") + ".");
            }
            catch (Exception ex)
            {
                MotorConnected = false;
                EncoderConnected = false;
                LastError = AppendError(LastError, "Phidget21 MotorControl connect failed: " + Unwrap(ex).Message);
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
            if (!MotorConnected)
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
                if (isPhidget21) SetPhidget21Velocity(velocity * 100.0);
                else SetProperty(motor, "TargetVelocity", velocity);
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
                if (isPhidget21) SetPhidget21Velocity(0.0);
                else if (motor != null) SetProperty(motor, "TargetVelocity", 0.0);
            }
            catch { }
        }

        public void ResetEncoderZero()
        {
            if (!EncoderConnected) return;
            try
            {
                if (isPhidget21) SetPhidget21EncoderPosition(0);
                else SetProperty(encoder, "Position", 0);
                EncoderCounts = 0;
            }
            catch (Exception ex)
            {
                LastError = "Encoder zero failed: " + Unwrap(ex).Message;
            }
        }

        public void RefreshEncoderPosition()
        {
            if (!EncoderConnected) return;
            try
            {
                EncoderCounts = isPhidget21 ? GetPhidget21EncoderPosition() : GetLongProperty(encoder, "Position");
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
            TryInvokeAny(motorControl21, "close");
            TryDispose(motorControl21);
        }

        private void SetPhidget21Velocity(double velocityPercent)
        {
            var motors = GetMemberValue(motorControl21, "motors") ?? GetMemberValue(motorControl21, "Motors");
            var motor0 = GetIndexedValue(motors, 0);
            SetPropertyAny(motor0, "Velocity", Clamp(velocityPercent, -100.0, 100.0));
        }

        private long GetPhidget21EncoderPosition()
        {
            var encoders = GetMemberValue(motorControl21, "encoders") ?? GetMemberValue(motorControl21, "Encoders");
            var enc0 = GetIndexedValue(encoders, 0);
            return GetLongPropertyAny(enc0, "Position");
        }

        private void SetPhidget21EncoderPosition(long position)
        {
            var encoders = GetMemberValue(motorControl21, "encoders") ?? GetMemberValue(motorControl21, "Encoders");
            var enc0 = GetIndexedValue(encoders, 0);
            SetPropertyAny(enc0, "Position", position);
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }

            foreach (var name in new[] { "Phidget21.NET", "Phidgets", "Phidget22.NET", "Phidget22" })
            {
                try
                {
                    var asm = Assembly.Load(name);
                    var t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { }
            }

            foreach (var path in GetCandidateAssemblyPaths())
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    var asm = Assembly.LoadFrom(path);
                    var t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { }
            }

            return null;
        }

        private static IEnumerable<string> GetCandidateAssemblyPaths()
        {
            var dlls = new[] { "Phidget21.NET.dll", "Phidgets.dll", "phidget21.NET.dll", "Phidget22.NET.dll", "Phidget22.dll" };
            var dirs = new[]
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Phidgets"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Phidgets")
            };

            foreach (var dir in dirs)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                foreach (var dll in dlls)
                {
                    yield return Path.Combine(dir, dll);
                }
            }
        }

        private static object GetMemberValue(object target, string name)
        {
            if (target == null) return null;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
            var prop = target.GetType().GetProperty(name, flags);
            if (prop != null) return prop.GetValue(target, null);
            var field = target.GetType().GetField(name, flags);
            return field == null ? null : field.GetValue(target);
        }

        private static object GetIndexedValue(object target, int index)
        {
            if (target == null) return null;
            var prop = target.GetType().GetProperties().FirstOrDefault(p => p.GetIndexParameters().Length == 1);
            if (prop != null) return prop.GetValue(target, new object[] { index });
            var method = target.GetType().GetMethods().FirstOrDefault(m => m.Name == "get_Item" && m.GetParameters().Length == 1);
            return method == null ? null : method.Invoke(target, new object[] { index });
        }

        private static void SetProperty(object target, string name, object value)
        {
            var prop = target.GetType().GetProperty(name);
            if (prop == null || !prop.CanWrite) return;
            var converted = Convert.ChangeType(value, prop.PropertyType);
            prop.SetValue(target, converted, null);
        }

        private static void SetPropertyAny(object target, string name, object value)
        {
            if (target == null) throw new InvalidOperationException("Target is null for property " + name);
            var prop = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (prop == null || !prop.CanWrite) throw new MissingMemberException(target.GetType().FullName, name);
            var converted = Convert.ChangeType(value, prop.PropertyType);
            prop.SetValue(target, converted, null);
        }

        private static long GetLongProperty(object target, string name)
        {
            var prop = target.GetType().GetProperty(name);
            if (prop == null) return 0;
            return Convert.ToInt64(prop.GetValue(target, null));
        }

        private static long GetLongPropertyAny(object target, string name)
        {
            if (target == null) return 0;
            var prop = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (prop == null) return 0;
            return Convert.ToInt64(prop.GetValue(target, null));
        }

        private static void Invoke(object target, string methodName, params object[] args)
        {
            var method = target.GetType().GetMethods().FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == args.Length);
            if (method == null) throw new MissingMethodException(target.GetType().FullName, methodName);
            method.Invoke(target, args);
        }

        private static void InvokeAny(object target, string methodName, params object[] args)
        {
            var method = target.GetType().GetMethods().FirstOrDefault(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase) && m.GetParameters().Length == args.Length);
            if (method == null) throw new MissingMethodException(target.GetType().FullName, methodName);
            method.Invoke(target, args);
        }

        private static void TryInvoke(object target, string methodName)
        {
            try { if (target != null) Invoke(target, methodName); } catch { }
        }

        private static void TryInvokeAny(object target, string methodName)
        {
            try { if (target != null) InvokeAny(target, methodName); } catch { }
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
        public string Driver { get; set; } = "phidget21";
        public int SerialNumber { get; set; }
        public int OpenTimeoutMilliseconds { get; set; } = 5000;
        public double MaximumTargetVelocity { get; set; } = 0.35;
        public double Acceleration { get; set; } = 4.0;
        public bool InvertMotorOutput { get; set; }
    }
}
