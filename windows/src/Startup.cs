using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Countdown
{
    /// <summary>"Start with Windows": the per-user Run key, as Task Manager's Startup tab sees it.</summary>
    internal static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string ValueName = "Countdown";

        static string ExecutablePath => Application.ExecutablePath;

        /// <summary>
        /// On only when the Run entry points at this copy of the app and Task Manager has not
        /// disabled it (its StartupApproved flag is odd when disabled).
        /// </summary>
        public static bool IsEnabled()
        {
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKey);
                if (!(run?.GetValue(ValueName) is string command)) return false;
                if (!string.Equals(command.Trim().Trim('"'), ExecutablePath, StringComparison.OrdinalIgnoreCase)) return false;
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
                return !(approved?.GetValue(ValueName) is byte[] state && state.Length > 0 && (state[0] & 1) == 1);
            }
            catch
            {
                return false;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                run.SetValue(ValueName, "\"" + ExecutablePath + "\"", RegistryValueKind.String);
                // Turning it on here also clears a "disabled" left by Task Manager.
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
                approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            else
            {
                run.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
    }
}
