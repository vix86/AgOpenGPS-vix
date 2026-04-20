using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Phidget22;
using Tinkerforge;

namespace CereaBridge
{
    internal sealed partial class BridgeService
    {
        private readonly object _sync = new object();
        private short _imuHeading16;
        private short _imuRoll16;

        private void OpenDevices()
        {
            OpenPhidgets();
            OpenImu();
        }

        private void CloseDevices()
        {
            try
            {
                if (_motor != null)
                {
                    _motor.TargetVelocity = 0;
                    _motor.Close();
                }
            }
            catch
            {
            }
            finally
            {
                _motor = null;
                IsMotorConnected = false;
            }

            try
            {
                if (_encoder != null)
                {
                    _encoder.Close();
                }
            }
            catch
            {
            }
            finally
            {
                _encoder = null;
                IsEncoderConnected = false;
            }

            try
            {
                if (_ipcon != null)
                {
                    var disconnect = _ipcon.GetType().GetMethod("Disconnect", BindingFlags.Instance | BindingFlags.Public);
                    disconnect?.Invoke(_ipcon, null);
                }
            }
            catch
            {
            }
            finally
            {
                _imu = null;
                _ipcon = null;
                IsImuConnected = false;
            }
        }

        private void BeginReceive(UdpClient client)
        {
            var thread = new Thread(() => ReceiveLoop(client))
            {
                IsBackground = true,
                Name = "CereaBridge UDP"
            };
            thread.Start();
        }

        private void ReceiveLoop(UdpClient client)
        {
            while (_running)
            {
                try
                {
                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    var bytes = client.Receive(ref remote);
                    HandleReceived(bytes);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    if (!_running)
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("CereaBridge receive error: " + ex.Message);
                    Thread.Sleep(20);
                }
            }
        }

        private void HandleReceived(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 5)
            {
                return;
            }

            if (bytes[0] != 0x80 || bytes[1] != 0x81)
            {
                return;
            }

            switch (bytes[3])
            {
                case 0xFE:
                    ApplySteerData(bytes);
                    break;
                case 0xFC:
                    ApplySteerSettings(bytes);
                    break;
                case 0xFB:
                    ApplySteerConfig(bytes);
                    break;
            }
        }

        private void ApplySteerData(byte[] bytes)
        {
            if (bytes.Length < 13)
            {
                return;
            }

            lock (_sync)
            {
                _speedKph = ReadUInt16(bytes, 5) / 10.0;
                _desiredAngleDeg = ReadInt16(bytes, 8) / 100.0;

                var status = bytes[7];
                _autosteerEnabled = (status & 0x01) != 0 || (status & 0x02) != 0 || Math.Abs(_desiredAngleDeg) > 0.01;
            }
        }

        private void ApplySteerSettings(byte[] bytes)
        {
            if (bytes.Length < 13)
            {
                return;
            }

            lock (_sync)
            {
                _kp = bytes[5];
                _highPwm = bytes[6];
                _minPwm = bytes[8];

                if (bytes[9] > 0)
                {
                    _countsPerDegree = bytes[9];
                }

                _wasOffset = ReadInt16(bytes, 10);
            }
        }

        private void ApplySteerConfig(byte[] bytes)
        {
            if (bytes.Length >= 8 && bytes[7] > 0)
            {
                lock (_sync)
                {
                    _minSpeedX10 = bytes[7];
                }
            }
        }

        private void OnTelemetryTick(object? state)
        {
            try
            {
                UpdateImuSample();
                var actualAngleDeg = GetActualSteerAngleDegrees();
                UpdateMotor(actualAngleDeg);
                SendSteerModulePacket(actualAngleDeg);
            }
            catch (Exception ex)
            {
                Console.WriteLine("CereaBridge telemetry error: " + ex.Message);
            }
        }

        private void OnHelloTick(object? state)
        {
            try
            {
                SendAutoSteerHelloPacket(GetActualSteerAngleDegrees(), GetRawWasCountsForHello());
            }
            catch (Exception ex)
            {
                Console.WriteLine("CereaBridge hello error: " + ex.Message);
            }
        }

        private void OpenPhidgets()
        {
            if (!_cfg.UsePhidgets)
            {
                return;
            }

            try
            {
                var motor = new DCMotor();
                var motorSerial = _cfg.GetEffectiveMotorSerialNumber();
                if (motorSerial > 0)
                {
                    motor.DeviceSerialNumber = motorSerial;
                }

                motor.Channel = _cfg.PhidgetsMotorChannel;
                motor.Open(5000);
                TrySetMotorAcceleration(motor);
                motor.TargetVelocity = 0;
                _motor = motor;
                IsMotorConnected = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Phidgets motor open failed: " + ex.Message);
                _motor = null;
                IsMotorConnected = false;
            }

            try
            {
                var encoder = new Encoder();
                var encoderSerial = _cfg.GetEffectiveEncoderSerialNumber();
                if (encoderSerial > 0)
                {
                    encoder.DeviceSerialNumber = encoderSerial;
                }

                encoder.Channel = _cfg.PhidgetsEncoderChannel;
                encoder.Open(5000);
                _encoder = encoder;
                IsEncoderConnected = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Phidgets encoder open failed: " + ex.Message);
                _encoder = null;
                IsEncoderConnected = false;
            }
        }

        private static void TrySetMotorAcceleration(DCMotor motor)
        {
            try
            {
                motor.Acceleration = motor.MaxAcceleration;
            }
            catch
            {
            }
        }

        private void OpenImu()
        {
            if (!_cfg.UseImuBrick || string.IsNullOrWhiteSpace(_cfg.ImuUid))
            {
                return;
            }

            try
            {
                var ipcon = new IPConnection();
                var imu = new BrickIMUV2(_cfg.ImuUid, ipcon);
                ipcon.Connect(_cfg.ImuHost, _cfg.ImuPort);
                _ipcon = ipcon;
                _imu = imu;
                UpdateImuSample();
                IsImuConnected = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("IMU Brick open failed: " + ex.Message);
                _imu = null;
                _ipcon = null;
                IsImuConnected = false;
            }
        }

        private double GetActualSteerAngleDegrees()
        {
            if (_encoder == null || !IsEncoderConnected)
            {
                return 0;
            }

            try
            {
                var counts = Convert.ToDouble(_encoder.Position, CultureInfo.InvariantCulture);
                if (_cfg.ReverseWas)
                {
                    counts = -counts;
                }

                var countsPerDegree = _countsPerDegree <= 0.0 ? _cfg.CountsPerDegreeFallback : _countsPerDegree;
                if (countsPerDegree <= 0.0)
                {
                    countsPerDegree = 1.0;
                }

                return (counts - _wasOffset) / countsPerDegree;
            }
            catch
            {
                return 0;
            }
        }

        private short GetRawWasCountsForHello()
        {
            if (_encoder == null || !IsEncoderConnected)
            {
                return 0;
            }

            try
            {
                var counts = _encoder.Position;
                if (_cfg.ReverseWas)
                {
                    counts = -counts;
                }

                if (counts > short.MaxValue) counts = short.MaxValue;
                if (counts < short.MinValue) counts = short.MinValue;
                return (short)counts;
            }
            catch
            {
                return 0;
            }
        }

        private void UpdateMotor(double actualAngleDeg)
        {
            if (_motor == null || !IsMotorConnected)
            {
                _lastPwm = 0;
                return;
            }

            double desiredAngleDeg;
            double speedKph;
            bool autosteerEnabled;
            byte kp;
            byte highPwm;
            byte minPwm;
            byte minSpeedX10;

            lock (_sync)
            {
                desiredAngleDeg = _desiredAngleDeg;
                speedKph = _speedKph;
                autosteerEnabled = _autosteerEnabled;
                kp = _kp;
                highPwm = _highPwm;
                minPwm = _minPwm;
                minSpeedX10 = _minSpeedX10;
            }

            var requestedVelocity = 0.0;
            _lastPwm = 0;

            if (autosteerEnabled && speedKph * 10.0 >= minSpeedX10)
            {
                var errorDeg = desiredAngleDeg - actualAngleDeg;
                if (Math.Abs(errorDeg) >= _cfg.DeadbandDegrees)
                {
                    var pwm = Math.Abs(errorDeg) * Math.Max(1, kp) * Math.Max(0.01, _cfg.VelocityGainMultiplier);
                    if (pwm > 0 && pwm < minPwm)
                    {
                        pwm = minPwm;
                    }

                    if (highPwm > 0 && pwm > highPwm)
                    {
                        pwm = highPwm;
                    }

                    requestedVelocity = Math.Clamp(pwm / 255.0, 0.0, 1.0);
                    requestedVelocity *= Math.Clamp(_cfg.MaxMotorOutput, 0.0, 1.0);
                    requestedVelocity *= Math.Sign(errorDeg);

                    if (_cfg.ReverseMotor)
                    {
                        requestedVelocity = -requestedVelocity;
                    }

                    _lastPwm = (int)Math.Round(Math.Clamp(Math.Abs(requestedVelocity) * 255.0, 0.0, 255.0));
                }
            }

            try
            {
                _motor.TargetVelocity = requestedVelocity;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Phidgets motor write failed: " + ex.Message);
                _lastPwm = 0;
            }
        }

        private void UpdateImuSample()
        {
            if (_imu == null || !IsImuConnected)
            {
                _imuHeading16 = 0;
                _imuRoll16 = 0;
                return;
            }

            try
            {
                var method = _imu.GetType().GetMethod("GetOrientation", BindingFlags.Instance | BindingFlags.Public);
                if (method == null)
                {
                    return;
                }

                short heading;
                short roll;

                var parameters = method.GetParameters();
                if (parameters.Length >= 3)
                {
                    var args = new object[] { (short)0, (short)0, (short)0 };
                    method.Invoke(_imu, args);
                    heading = ConvertToInt16(args[0]);
                    roll = ConvertToInt16(args[1]);
                }
                else
                {
                    var result = method.Invoke(_imu, null);
                    heading = ReadMember(result, "Heading", "heading");
                    roll = ReadMember(result, "Roll", "roll");
                }

                if (_cfg.ReverseHeading)
                {
                    heading = NormalizeHeading16((short)(5760 - heading));
                }

                heading = NormalizeHeading16((short)(heading + _cfg.HeadingOffset16));

                if (_cfg.ReverseRoll)
                {
                    roll = (short)-roll;
                }

                _imuHeading16 = heading;
                _imuRoll16 = roll;
            }
            catch
            {
                _imuHeading16 = 0;
                _imuRoll16 = 0;
            }
        }

        private void SendSteerModulePacket(double actualAngleDeg)
        {
            var steerAngle100 = (short)Math.Round(Math.Clamp(actualAngleDeg * 100.0, short.MinValue, short.MaxValue));
            var bytes = new byte[14];
            bytes[0] = 0x80;
            bytes[1] = 0x81;
            bytes[2] = 0x7F;
            bytes[3] = 0xFD;
            bytes[4] = 8;
            WriteInt16(bytes, 5, steerAngle100);
            WriteInt16(bytes, 7, _imuHeading16);
            WriteInt16(bytes, 9, _imuRoll16);

            byte switches = 0;
            if (_cfg.WorkSwitchOn)
            {
                switches |= 0x01;
            }
            if (_cfg.SteerSwitchOn)
            {
                switches |= 0x02;
            }
            bytes[11] = switches;
            bytes[12] = (byte)Math.Clamp(_lastPwm, 0, 255);
            bytes[13] = 0xCC;

            _sender.Send(bytes, bytes.Length, _agioEndpoint);
        }

        private void SendAutoSteerHelloPacket(double actualAngleDeg, short rawWasCounts)
        {
            var steerAngle100 = (short)Math.Round(Math.Clamp(actualAngleDeg * 100.0, short.MinValue, short.MaxValue));
            var bytes = new byte[11];
            bytes[0] = 0x80;
            bytes[1] = 0x81;
            bytes[2] = 126;
            bytes[3] = 126;
            bytes[4] = 5;
            WriteInt16(bytes, 5, steerAngle100);
            WriteInt16(bytes, 7, rawWasCounts);

            byte switches = 0;
            if (_cfg.WorkSwitchOn)
            {
                switches |= 0x01;
            }
            if (_cfg.SteerSwitchOn)
            {
                switches |= 0x02;
            }
            bytes[9] = switches;
            bytes[10] = 0xCC;

            _sender.Send(bytes, bytes.Length, _agioEndpoint);
        }

        private static short ReadInt16(byte[] bytes, int lowIndex)
        {
            if (bytes.Length <= lowIndex + 1)
            {
                return 0;
            }

            return unchecked((short)(bytes[lowIndex] | (bytes[lowIndex + 1] << 8)));
        }

        private static ushort ReadUInt16(byte[] bytes, int lowIndex)
        {
            if (bytes.Length <= lowIndex + 1)
            {
                return 0;
            }

            return (ushort)(bytes[lowIndex] | (bytes[lowIndex + 1] << 8));
        }

        private static void WriteInt16(byte[] bytes, int lowIndex, short value)
        {
            bytes[lowIndex] = (byte)(value & 0xFF);
            bytes[lowIndex + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static short ConvertToInt16(object? value)
        {
            if (value == null)
            {
                return 0;
            }

            return Convert.ToInt16(value, CultureInfo.InvariantCulture);
        }

        private static short ReadMember(object? source, params string[] names)
        {
            if (source == null)
            {
                return 0;
            }

            var type = source.GetType();
            foreach (var name in names)
            {
                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (property != null)
                {
                    return ConvertToInt16(property.GetValue(source));
                }

                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (field != null)
                {
                    return ConvertToInt16(field.GetValue(source));
                }
            }

            return 0;
        }

        private static short NormalizeHeading16(short heading)
        {
            var normalized = heading % 5760;
            if (normalized < 0)
            {
                normalized += 5760;
            }
            return (short)normalized;
        }
    }
}
