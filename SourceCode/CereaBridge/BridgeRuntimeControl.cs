using System;
using System.Globalization;

namespace CereaBridge
{
    internal sealed partial class BridgeService
    {
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

        private double GetActualSteerAngleDegrees()
        {
            if (_cfg.IsRelativeNoWasMode())
            {
                return Math.Clamp(_cfg.RelativeFeedbackAngle, -_cfg.RelativeMaxCommandAngle, _cfg.RelativeMaxCommandAngle);
            }

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
            if (_cfg.IsRelativeNoWasMode())
            {
                return 0;
            }

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

            if (!_cfg.WorkSwitchOn)
            {
                try
                {
                    _motor.TargetVelocity = 0;
                }
                catch
                {
                }

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
            DateTime lastSteerDataUtc;

            lock (_sync)
            {
                desiredAngleDeg = _desiredAngleDeg;
                speedKph = _speedKph;
                autosteerEnabled = _autosteerEnabled;
                kp = _kp;
                highPwm = _highPwm;
                minPwm = _minPwm;
                minSpeedX10 = _minSpeedX10;
                lastSteerDataUtc = _lastSteerDataUtc;
            }

            if (lastSteerDataUtc == DateTime.MinValue || DateTime.UtcNow - lastSteerDataUtc > _steerCommandTimeout)
            {
                autosteerEnabled = false;
            }

            var requestedVelocity = 0.0;
            _lastPwm = 0;

            if (autosteerEnabled && speedKph * 10.0 >= minSpeedX10)
            {
                var commandDeg = _cfg.IsRelativeNoWasMode()
                    ? Math.Clamp(desiredAngleDeg, -_cfg.RelativeMaxCommandAngle, _cfg.RelativeMaxCommandAngle)
                    : desiredAngleDeg - actualAngleDeg;

                if (Math.Abs(commandDeg) >= _cfg.DeadbandDegrees)
                {
                    var gain = Math.Max(0.01, _cfg.VelocityGainMultiplier);
                    if (_cfg.IsRelativeNoWasMode())
                    {
                        gain *= Math.Max(0.01, _cfg.RelativeCommandGain);
                    }

                    var pwm = Math.Abs(commandDeg) * Math.Max(1, (int)kp) * gain;
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
                    requestedVelocity *= Math.Sign(commandDeg);

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
    }
}
