using System;
using System.Reflection;
using Phidget22;
using Tinkerforge;

namespace CereaBridge
{
    internal sealed partial class BridgeService
    {
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
    }
}
