using System;
using System.IO;

namespace AgOpenGPS.Hardware.CereaStyle
{
    public static class CereaStyleNoWasOptions
    {
        private const string FlagFileName = "CereaStyleNoWas.flag";

        public static bool IsEnabled()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FlagFileName);
                if (!File.Exists(path)) return false;
                var text = File.ReadAllText(path).Trim();
                return text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
