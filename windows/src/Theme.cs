using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Countdown
{
    internal static class Theme
    {
        const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        /// <summary>True when the taskbar is light ("Choose your mode" set to Light for Windows).</summary>
        public static bool TaskbarIsLight()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
            }
            catch
            {
                return false;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate int SetPreferredAppModeFn(int mode);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate void FlushMenuThemesFn();

        static bool loaded;
        static SetPreferredAppModeFn? setPreferredAppMode;
        static FlushMenuThemesFn? flushMenuThemes;

        /// <summary>
        /// Makes this app's popup menus dark on a dark taskbar and light on a light one, the way
        /// the taskbar's own menus look. Uses uxtheme's unnamed exports 135 and 136, which
        /// Windows 10 1903 and later have; on anything older it does nothing.
        /// </summary>
        public static void MatchMenusToTaskbar()
        {
            if (Environment.OSVersion.Version.Major < 10 || Environment.OSVersion.Version.Build < 18362) return;
            try
            {
                if (!loaded)
                {
                    loaded = true;
                    IntPtr uxtheme = Native.LoadLibraryEx("uxtheme.dll", IntPtr.Zero, Native.LOAD_LIBRARY_SEARCH_SYSTEM32);
                    if (uxtheme == IntPtr.Zero) return;
                    IntPtr setMode = Native.GetProcAddress(uxtheme, (IntPtr)135);
                    IntPtr flush = Native.GetProcAddress(uxtheme, (IntPtr)136);
                    if (setMode != IntPtr.Zero)
                        setPreferredAppMode = (SetPreferredAppModeFn)Marshal.GetDelegateForFunctionPointer(setMode, typeof(SetPreferredAppModeFn));
                    if (flush != IntPtr.Zero)
                        flushMenuThemes = (FlushMenuThemesFn)Marshal.GetDelegateForFunctionPointer(flush, typeof(FlushMenuThemesFn));
                }
                const int ForceDark = 2, ForceLight = 3;
                setPreferredAppMode?.Invoke(TaskbarIsLight() ? ForceLight : ForceDark);
                flushMenuThemes?.Invoke();
            }
            catch (Exception e)
            {
                Log.Write("menu theme: " + e.Message);
            }
        }
    }
}
