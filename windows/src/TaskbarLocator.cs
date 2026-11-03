using System;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace Countdown
{
    /// <summary>Where the primary taskbar is and where its notification area starts.</summary>
    internal sealed class TaskbarLayout
    {
        public IntPtr Taskbar;
        /// <summary>The taskbar window in screen pixels.</summary>
        public Native.RECT Bounds;
        public int ClientHeight;
        public int Dpi = 96;
        public bool IsHorizontal;
        public bool IsAtTop;
        /// <summary>Screen x where the notification area (tray icons and clock) begins.</summary>
        public int TrayLeft;
        public string TraySource = "";
    }

    internal static class TaskbarLocator
    {
        /// <summary>Notification area width assumed when neither Win32 nor UI Automation finds it.</summary>
        const int EstimatedTrayWidth = 240;

        public static TaskbarLayout? Find(bool synchronousAutomation = false)
        {
            IntPtr taskbar = Native.FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero || !Native.GetWindowRect(taskbar, out Native.RECT bounds)) return null;
            Native.GetClientRect(taskbar, out Native.RECT client);

            var layout = new TaskbarLayout
            {
                Taskbar = taskbar,
                Bounds = bounds,
                ClientHeight = client.Height,
                Dpi = Native.DpiOf(taskbar),
                IsHorizontal = bounds.Width > bounds.Height,
            };
            var screen = Screen.FromHandle(taskbar).Bounds;
            layout.IsAtTop = bounds.Top <= screen.Top && bounds.Bottom < screen.Bottom;

            if (TrayFromWindow(taskbar, bounds, out int left))
            {
                layout.TrayLeft = left;
                layout.TraySource = "TrayNotifyWnd";
            }
            else if (synchronousAutomation ? TryQuery(taskbar, out left) : AutomationTray.TryGetLeft(taskbar, out left))
            {
                layout.TrayLeft = left;
                layout.TraySource = "UI Automation";
            }
            else
            {
                layout.TrayLeft = bounds.Right - Native.Scale(EstimatedTrayWidth, layout.Dpi);
                layout.TraySource = "estimate";
            }
            return layout;
        }

        /// <summary>
        /// Windows 10 (and the Windows 11 builds that keep it) expose the notification area as
        /// TrayNotifyWnd. Its rectangle is only trusted when it sits in the taskbar's right part.
        /// </summary>
        static bool TrayFromWindow(IntPtr taskbar, Native.RECT bounds, out int left)
        {
            left = 0;
            IntPtr tray = Native.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            if (tray == IntPtr.Zero || !Native.IsWindowVisible(tray) || !Native.GetWindowRect(tray, out Native.RECT r)) return false;
            bool plausible = r.Width >= 16 && r.Height > 0
                && r.Left > bounds.Left + bounds.Width / 3
                && r.Right <= bounds.Right + 2;
            if (!plausible) return false;
            left = r.Left;
            return true;
        }

        static bool TryQuery(IntPtr taskbar, out int left)
        {
            left = AutomationTray.Query(taskbar);
            return left >= 0;
        }
    }

    /// <summary>
    /// The Windows 11 notification area is XAML, visible only through UI Automation: its buttons
    /// have class names starting with "SystemTray.". Queried on a pool thread (UI Automation
    /// must not run on the thread that owns windows inside the taskbar) and cached.
    /// </summary>
    internal static class AutomationTray
    {
        static readonly object Gate = new object();
        static int cachedLeft = -1;
        static IntPtr cachedFor = IntPtr.Zero;
        static DateTime refreshedUtc = DateTime.MinValue;
        static bool refreshing;

        public static bool TryGetLeft(IntPtr taskbar, out int left)
        {
            lock (Gate)
            {
                if (!refreshing && (DateTime.UtcNow - refreshedUtc).TotalSeconds >= 3)
                {
                    refreshing = true;
                    ThreadPool.QueueUserWorkItem(_ => Refresh(taskbar));
                }
                left = cachedLeft;
                return cachedLeft >= 0 && cachedFor == taskbar;
            }
        }

        static void Refresh(IntPtr taskbar)
        {
            int left = -1;
            try
            {
                left = Query(taskbar);
            }
            catch (Exception e)
            {
                Log.Write("automation: " + e.Message);
            }
            lock (Gate)
            {
                cachedLeft = left;
                cachedFor = taskbar;
                refreshedUtc = DateTime.UtcNow;
                refreshing = false;
            }
        }

        /// <summary>The leftmost "SystemTray.*" button's screen x, or -1.</summary>
        public static int Query(IntPtr taskbar)
        {
            AutomationElement root = AutomationElement.FromHandle(taskbar);
            var buttons = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            double best = double.MaxValue;
            foreach (AutomationElement button in buttons)
            {
                string className;
                System.Windows.Rect rect;
                try
                {
                    className = button.Current.ClassName ?? "";
                    rect = button.Current.BoundingRectangle;
                }
                catch (ElementNotAvailableException)
                {
                    continue;
                }
                if (!className.StartsWith("SystemTray.", StringComparison.Ordinal) || rect.IsEmpty || rect.Width <= 0) continue;
                best = Math.Min(best, rect.Left);
            }
            return best == double.MaxValue ? -1 : (int)Math.Round(best);
        }
    }
}
