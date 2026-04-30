using System;

namespace AgOpenGPS.Hardware.CereaStyle
{
    // Cerea-style controller for Phidgets motor + encoder without real WAS.
    // Encoder is motor/steering-wheel travel feedback only, not wheel angle.
    public sealed class CereaStyleNoWasController
    {
        private readonly CereaStyleNoWasSettings settings;
        private double lastCommand;
        private long lastEncoderCounts;
        private DateTime lastEncoderMoveUtc = DateTime.MinValue;

        public CereaStyleNoWasController(CereaStyleNoWasSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public bool IsArmed { get; private set; }
        public bool IsRunning { get; private set; }
        public string LastFault { get; private set; } = string.Empty;

        public void Arm()
        {
            LastFault = string.Empty;
            IsArmed = true;
            IsRunning = false;
            lastCommand = 0.0;
        }

        public void Start()
        {
            if (!IsArmed)
            {
                Fault("Start refused: motor is not armed.");
                return;
            }

            LastFault = string.Empty;
            IsRunning = true;
            lastCommand = 0.0;
        }

        public void Stop()
        {
            IsRunning = false;
            lastCommand = 0.0;
        }

        public void Disarm()
        {
            IsRunning = false;
            IsArmed = false;
            lastCommand = 0.0;
        }

        public CereaStyleNoWasOutput Update(CereaStyleNoWasInput input)
        {
            var output = new CereaStyleNoWasOutput
            {
                IsArmed = IsArmed,
                IsRunning = IsRunning,
                SafetyState = IsArmed ? (IsRunning ? CereaStyleSafetyState.Running : CereaStyleSafetyState.ArmedStopped) : CereaStyleSafetyState.Disabled,
                Fault = LastFault,
                EncoderCounts = input.EncoderCounts
            };

            if (!IsArmed || !IsRunning)
            {
                output.MotorCommand = 0.0;
                return output;
            }

            if (!input.GpsValid) return FaultOutput(output, "GPS invalid.");
            if (input.GpsAgeMilliseconds > settings.GpsWatchdogMilliseconds) return FaultOutput(output, "GPS watchdog timeout.");
            if (settings.RequireRtkFix && !input.RtkFixed) return FaultOutput(output, "RTK FIX lost.");
            if (!input.AbLineValid) return FaultOutput(output, "AB line invalid.");
            if (Math.Abs(input.EncoderCounts) >= settings.EncoderHardLimitCounts) return FaultOutput(output, "Encoder hard limit reached.");

            if (input.SpeedKph < settings.MinimumSpeedKph || input.SpeedKph > settings.MaximumSpeedKph)
            {
                lastCommand = Approach(lastCommand, 0.0, settings.CommandRateLimitPerCycle);
                output.MotorCommand = lastCommand;
                output.Message = "Speed outside steering range.";
                return output;
            }

            TrackEncoderMotion(input);
            if (settings.StallDetectionEnabled && IsMotorStalled(input)) return FaultOutput(output, "Encoder did not move while motor command was active.");

            var xteAngleDeg = Math.Atan2(input.CrossTrackErrorMeters, Math.Max(0.25, settings.LookAheadMeters)) * 180.0 / Math.PI;
            var command = settings.CrossTrackGain * xteAngleDeg + settings.HeadingGain * input.HeadingErrorDegrees;
            if (settings.InvertGuidanceCommand) command = -command;

            command = ApplyEncoderCentering(command, input.EncoderCounts);
            command = ApplyEncoderSoftLimit(command, input.EncoderCounts);
            command = Clamp(command, -settings.MaximumCommand, settings.MaximumCommand);
            if (Math.Abs(command) < settings.MinimumCommand) command = 0.0;

            command = Approach(lastCommand, command, settings.CommandRateLimitPerCycle);
            lastCommand = command;

            output.MotorCommand = command;
            output.CrossTrackAngleDegrees = xteAngleDeg;
            output.Fault = LastFault;
            return output;
        }

        private double ApplyEncoderCentering(double command, long encoderCounts)
        {
            if (settings.EncoderSoftLimitCounts <= 0 || settings.EncoderCenteringGain <= 0.0) return command;
            var normalizedEncoder = Clamp((double)encoderCounts / settings.EncoderSoftLimitCounts, -1.0, 1.0);
            return command - normalizedEncoder * settings.EncoderCenteringGain;
        }

        private double ApplyEncoderSoftLimit(double command, long encoderCounts)
        {
            var soft = settings.EncoderSoftLimitCounts;
            if (soft <= 0 || Math.Abs(encoderCounts) < soft) return command;
            if (encoderCounts > soft && command > 0.0) return 0.0;
            if (encoderCounts < -soft && command < 0.0) return 0.0;
            return command;
        }

        private void TrackEncoderMotion(CereaStyleNoWasInput input)
        {
            if (Math.Abs(input.EncoderCounts - lastEncoderCounts) >= settings.EncoderMovementThresholdCounts)
            {
                lastEncoderMoveUtc = input.NowUtc;
                lastEncoderCounts = input.EncoderCounts;
            }
        }

        private bool IsMotorStalled(CereaStyleNoWasInput input)
        {
            if (Math.Abs(lastCommand) < settings.StallCommandThreshold) return false;
            if (lastEncoderMoveUtc == DateTime.MinValue)
            {
                lastEncoderMoveUtc = input.NowUtc;
                return false;
            }
            return (input.NowUtc - lastEncoderMoveUtc).TotalMilliseconds > settings.StallTimeoutMilliseconds;
        }

        private CereaStyleNoWasOutput FaultOutput(CereaStyleNoWasOutput output, string reason)
        {
            Fault(reason);
            output.MotorCommand = 0.0;
            output.IsArmed = IsArmed;
            output.IsRunning = IsRunning;
            output.SafetyState = CereaStyleSafetyState.Fault;
            output.Fault = LastFault;
            return output;
        }

        private void Fault(string reason)
        {
            LastFault = reason ?? string.Empty;
            IsRunning = false;
            IsArmed = false;
            lastCommand = 0.0;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static double Approach(double current, double target, double maxDelta)
        {
            if (maxDelta <= 0.0) return target;
            var delta = target - current;
            if (delta > maxDelta) return current + maxDelta;
            if (delta < -maxDelta) return current - maxDelta;
            return target;
        }
    }

    public sealed class CereaStyleNoWasSettings
    {
        public bool RequireRtkFix { get; set; } = true;
        public double MinimumSpeedKph { get; set; } = 0.4;
        public double MaximumSpeedKph { get; set; } = 20.0;
        public int GpsWatchdogMilliseconds { get; set; } = 300;
        public double LookAheadMeters { get; set; } = 8.0;
        public double CrossTrackGain { get; set; } = 0.18;
        public double HeadingGain { get; set; } = 0.035;
        public double MaximumCommand { get; set; } = 0.45;
        public double MinimumCommand { get; set; } = 0.02;
        public double CommandRateLimitPerCycle { get; set; } = 0.05;
        public bool InvertGuidanceCommand { get; set; }
        public long EncoderSoftLimitCounts { get; set; } = 18000;
        public long EncoderHardLimitCounts { get; set; } = 24000;
        public int EncoderMovementThresholdCounts { get; set; } = 4;
        public double EncoderCenteringGain { get; set; } = 0.08;
        public bool StallDetectionEnabled { get; set; }
        public double StallCommandThreshold { get; set; } = 0.12;
        public int StallTimeoutMilliseconds { get; set; } = 750;
    }

    public sealed class CereaStyleNoWasInput
    {
        public bool GpsValid { get; set; }
        public bool RtkFixed { get; set; }
        public bool AbLineValid { get; set; }
        public int GpsAgeMilliseconds { get; set; }
        public double SpeedKph { get; set; }
        public double CrossTrackErrorMeters { get; set; }
        public double HeadingErrorDegrees { get; set; }
        public long EncoderCounts { get; set; }
        public DateTime NowUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class CereaStyleNoWasOutput
    {
        public bool IsArmed { get; set; }
        public bool IsRunning { get; set; }
        public CereaStyleSafetyState SafetyState { get; set; }
        public double MotorCommand { get; set; }
        public double CrossTrackAngleDegrees { get; set; }
        public long EncoderCounts { get; set; }
        public string Fault { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public enum CereaStyleSafetyState
    {
        Disabled,
        ArmedStopped,
        Running,
        Fault
    }
}
