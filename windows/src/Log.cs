using System;
using System.IO;

namespace Countdown
{
    /// <summary>A debug log at %TEMP%\Countdown.log, written only when COUNTDOWN_DEBUG=1.</summary>
    internal static class Log
    {
        static readonly string? FilePath =
            Environment.GetEnvironmentVariable("COUNTDOWN_DEBUG") == "1"
                ? Path.Combine(Path.GetTempPath(), "Countdown.log")
                : null;

        public static void Write(string message)
        {
            if (FilePath == null) return;
            try
            {
                File.AppendAllText(FilePath, DateTime.Now.ToString("HH:mm:ss.fff ") + message + Environment.NewLine);
            }
            catch
            {
                // Logging must never break the app.
            }
        }
    }
}
