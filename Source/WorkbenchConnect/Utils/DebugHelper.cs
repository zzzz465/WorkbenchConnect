using Verse;

namespace WorkbenchConnect.Utils
{
    public static class DebugHelper
    {
        public static void Log(string message)
        {
            if (WorkbenchConnectMod.settings?.enableDebugLogging == true)
            {
                Verse.Log.Message($"[WorkbenchConnect] {message}");
            }
        }

        public static void Warning(string message)
        {
            Verse.Log.Warning($"[WorkbenchConnect] {message}");
        }

        public static void Error(string message)
        {
            Verse.Log.Error($"[WorkbenchConnect] {message}");
        }
    }
}