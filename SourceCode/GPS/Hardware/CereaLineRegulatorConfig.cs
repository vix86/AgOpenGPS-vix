using System;
using System.Globalization;
using System.IO;

namespace AgOpenGPS
{
    internal sealed class CereaLineRegulatorConfig
    {
        public const string FileName = "CereaLineDryRun.ini";

        public bool Enabled = false;
        public bool Reverse = false;
        public double LineGain = 0.020;
        public double HeadingGain = 0.035;
        public double Deadband = 0.015;
        public double MinOutput = 0.08;
        public double MaxOutput = 0.60;
        public double MaxStep = 0.08;
        public int TickMs = 50;

        public static CereaLineRegulatorConfig LoadOrCreateDefault()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
            if (!File.Exists(path))
            {
                File.WriteAllLines(path, new[]
                {
                    "# Cerea-style dry-run regulator. No hardware output here.",
                    "Enabled=false",
                    "Reverse=false",
                    "LineGain=0.020",
                    "HeadingGain=0.035",
                    "Deadband=0.015",
                    "MinOutput=0.08",
                    "MaxOutput=0.60",
                    "MaxStep=0.08",
                    "TickMs=50"
                });
            }

            var cfg = new CereaLineRegulatorConfig();
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                int idx = line.IndexOf('=');
                if (idx <= 0) continue;

                string key = line.Substring(0, idx).Trim();
                string value = line.Substring(idx + 1).Trim();
                cfg.Apply(key, value);
            }

            cfg.Sanitize();
            return cfg;
        }

        public CereaLineRegulator CreateRegulator()
        {
            return new CereaLineRegulator
            {
                Enabled = Enabled,
                Reverse = Reverse,
                LineGain = LineGain,
                HeadingGain = HeadingGain,
                Deadband = Deadband,
                MinOutput = MinOutput,
                MaxOutput = MaxOutput,
                MaxStep = MaxStep
            };
        }

        private void Apply(string key, string value)
        {
            switch (key)
            {
                case "Enabled": Enabled = ParseBool(value, Enabled); break;
                case "Reverse": Reverse = ParseBool(value, Reverse); break;
                case "LineGain": LineGain = ParseDouble(value, LineGain); break;
                case "HeadingGain": HeadingGain = ParseDouble(value, HeadingGain); break;
                case "Deadband": Deadband = ParseDouble(value, Deadband); break;
                case "MinOutput": MinOutput = ParseDouble(value, MinOutput); break;
                case "MaxOutput": MaxOutput = ParseDouble(value, MaxOutput); break;
                case "MaxStep": MaxStep = ParseDouble(value, MaxStep); break;
                case "TickMs": TickMs = ParseInt(value, TickMs); break;
            }
        }

        private void Sanitize()
        {
            if (Deadband < 0) Deadband = 0;
            if (MinOutput < 0) MinOutput = 0;
            if (MaxOutput <= 0 || MaxOutput > 1) MaxOutput = 0.60;
            if (MinOutput > MaxOutput) MinOutput = MaxOutput;
            if (MaxStep <= 0 || MaxStep > 1) MaxStep = 0.08;
            if (TickMs < 20) TickMs = 20;
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static double ParseDouble(string value, double fallback)
        {
            double parsed;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            bool parsed;
            return bool.TryParse(value, out parsed) ? parsed : fallback;
        }
    }
}
