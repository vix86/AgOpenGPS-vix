using System;

namespace AgOpenGPS
{
    /// <summary>
    /// Dry-run Cerea-style line regulator.
    /// This class only calculates a normalized command value and does not access hardware.
    /// </summary>
    internal sealed class CereaLineRegulator
    {
        public bool Enabled { get; set; }
        public bool Reverse { get; set; }
        public double LineGain { get; set; } = 0.020;
        public double HeadingGain { get; set; } = 0.035;
        public double Deadband { get; set; } = 0.015;
        public double MinOutput { get; set; } = 0.08;
        public double MaxOutput { get; set; } = 0.60;
        public double MaxStep { get; set; } = 0.08;

        private double lastOutput;

        public double Update(double lineErrorMeters, double headingErrorDegrees, bool enabled, double speedKph, double minSpeedKph)
        {
            if (!Enabled || !enabled || speedKph < minSpeedKph)
            {
                return Ramp(0.0);
            }

            double output = lineErrorMeters * LineGain + headingErrorDegrees * HeadingGain;

            if (Reverse)
            {
                output = -output;
            }

            output = Limit(output, -MaxOutput, MaxOutput);

            if (Math.Abs(output) < Deadband)
            {
                output = 0.0;
            }
            else if (Math.Abs(output) < MinOutput)
            {
                output = Math.Sign(output) * MinOutput;
            }

            return Ramp(output);
        }

        public void Reset()
        {
            lastOutput = 0.0;
        }

        private double Ramp(double target)
        {
            double step = Math.Max(0.001, MaxStep);
            double delta = target - lastOutput;

            if (delta > step)
            {
                target = lastOutput + step;
            }
            else if (delta < -step)
            {
                target = lastOutput - step;
            }

            lastOutput = Limit(target, -1.0, 1.0);
            return lastOutput;
        }

        private static double Limit(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
