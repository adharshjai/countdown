using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace Countdown
{
    /// <summary>Where the primary taskbar is, where its notification area starts, and what it holds.</summary>
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
        /// <summary>Screen x spans of the taskbar's buttons (Start, search, apps); null until scanned.</summary>
        public IReadOnlyList<(int left, int right)>? Occupied;
    }

    internal static class TaskbarLocator
    {
        /// <summary>Notification area width assumed when neither Win32 nor UI Automation finds it.</summary>
        const int EstimatedTrayWidth = 240;

        public static TaskbarLayout? Find(bool synchronousScan = false)
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

            TaskbarSnapshot? snapshot = synchronousScan ? TaskbarScan.TryTake(taskbar) : TaskbarScan.Latest(taskbar);
            layout.Occupied = snapshot?.Occupied;

            if (TrayFromWindow(taskbar, bounds, out int left))
            {
                layout.TrayLeft = left;
                layout.TraySource = "TrayNotifyWnd";
            }
            else if (snapshot != null && snapshot.TrayLeft >= 0)
            {
                layout.TrayLeft = snapshot.TrayLeft;
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
        /// Windows 10, and the Windows 11 builds that keep it, expose the notification area as
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
    }

    /// <summary>One UI Automation pass over the taskbar.</summary>
    internal sealed class TaskbarSnapshot
    {
        public TaskbarSnapshot(IntPtr taskbar, int trayLeft, IReadOnlyList<(int left, int right)> occupied)
        {
            Taskbar = taskbar;
            TrayLeft = trayLeft;
            Occupied = occupied;
        }

        public IntPtr Taskbar { get; }
        /// <summary>The leftmost Windows 11 notification-area button ("SystemTray.*"), or -1.</summary>
        public int TrayLeft { get; }
        /// <summary>Every other visible button and text field: Start, search, widgets, apps.</summary>
        public IReadOnlyList<(int left, int right)> Occupied { get; }
    }

    /// <summary>
    /// Scans the taskbar with UI Automation every two seconds on a pool thread and caches the
    /// result. Windows 11 draws its taskbar in XAML, visible only this way; on Windows 10 it
    /// sees the same buttons as the window tree. UI Automation must not run on the thread
    /// that owns a window inside the taskbar, which is why this never runs on the UI thread.
    /// </summary>
    internal static class TaskbarScan
    {
        const double RefreshSeconds = 2;
        static readonly object Gate = new object();
        static TaskbarSnapshot? latest;
        static DateTime refreshedUtc = DateTime.MinValue;
        static bool refreshing;

        /// <summary>The newest snapshot of this taskbar (null before the first), refreshing if due.</summary>
        public static TaskbarSnapshot? Latest(IntPtr taskbar)
        {
            lock (Gate)
            {
                if (!refreshing && (DateTime.UtcNow - refreshedUtc).TotalSeconds >= RefreshSeconds)
                {
                    refreshing = true;
                    ThreadPool.QueueUserWorkItem(_ => Refresh(taskbar));
                }
                return latest != null && latest.Taskbar == taskbar ? latest : null;
            }
        }

        static void Refresh(IntPtr taskbar)
        {
            TaskbarSnapshot? snapshot = TryTake(taskbar);
            lock (Gate)
            {
                if (snapshot != null) latest = snapshot;
                refreshedUtc = DateTime.UtcNow;
                refreshing = false;
            }
        }

        public static TaskbarSnapshot? TryTake(IntPtr taskbar)
        {
            try
            {
                return Take(taskbar);
            }
            catch (Exception e)
            {
                Log.Write("scan failed: " + e.Message);
                return null;
            }
        }

        static TaskbarSnapshot Take(IntPtr taskbar)
        {
            AutomationElement root = AutomationElement.FromHandle(taskbar);
            var condition = new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            int trayLeft = -1;
            var occupied = new List<(int left, int right)>();
            foreach (AutomationElement element in root.FindAll(TreeScope.Descendants, condition))
            {
                string className, name;
                System.Windows.Rect r;
                try
                {
                    if (element.Current.IsOffscreen) continue;
                    className = element.Current.ClassName ?? "";
                    name = element.Current.Name ?? "";
                    r = element.Current.BoundingRectangle;
                }
                catch (ElementNotAvailableException)
                {
                    continue;
                }
                if (r.IsEmpty || r.Width <= 0 || r.Height <= 0) continue;
                if (className.StartsWith("SystemTray.", StringComparison.Ordinal))
                {
                    int left = (int)Math.Floor(r.Left);
                    trayLeft = trayLeft < 0 ? left : Math.Min(trayLeft, left);
                    continue;
                }
                if (name == TaskbarLabel.Caption) continue;
                occupied.Add(((int)Math.Floor(r.Left), (int)Math.Ceiling(r.Right)));
            }
            return new TaskbarSnapshot(taskbar, trayLeft, occupied);
        }
    }
}
