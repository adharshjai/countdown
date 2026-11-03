using System;
using System.Windows.Automation;

namespace Countdown
{
    /// <summary><c>Countdown.exe --diagnose</c>: what the app sees of the taskbar, for bug reports.</summary>
    internal static class Diagnostics
    {
        public static void Print()
        {
            var o = Console.Out;
            o.WriteLine($"Windows {Environment.OSVersion.Version}, taskbar light: {Theme.TaskbarIsLight()}");
            IntPtr taskbar = Native.FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero)
            {
                o.WriteLine("No Shell_TrayWnd: Explorer's taskbar is not running.");
                return;
            }
            Native.GetWindowRect(taskbar, out Native.RECT bounds);
            o.WriteLine($"Shell_TrayWnd 0x{taskbar.ToInt64():X} {bounds} dpi {Native.DpiOf(taskbar)}");
            for (IntPtr child = Native.FindWindowEx(taskbar, IntPtr.Zero, null, null);
                 child != IntPtr.Zero;
                 child = Native.FindWindowEx(taskbar, child, null, null))
            {
                Native.GetWindowRect(child, out Native.RECT r);
                o.WriteLine($"  child {Native.ClassOf(child)} \"{Native.TitleOf(child)}\" {r} visible={Native.IsWindowVisible(child)}");
            }

            try
            {
                var all = AutomationElement.FromHandle(taskbar).FindAll(TreeScope.Descendants, Condition.TrueCondition);
                o.WriteLine($"UI Automation: {all.Count} elements; those in the right half:");
                double middle = bounds.Left + bounds.Width / 2.0;
                foreach (AutomationElement element in all)
                {
                    try
                    {
                        var r = element.Current.BoundingRectangle;
                        if (r.IsEmpty || r.Left < middle) continue;
                        o.WriteLine($"  {element.Current.ControlType.ProgrammaticName} class=\"{element.Current.ClassName}\" " +
                            $"name=\"{element.Current.Name}\" [{r.Left:0},{r.Top:0} {r.Width:0}x{r.Height:0}]");
                    }
                    catch (ElementNotAvailableException)
                    {
                    }
                }
                o.WriteLine($"UI Automation tray left: {AutomationTray.Query(taskbar)}");
            }
            catch (Exception e)
            {
                o.WriteLine("UI Automation failed: " + e.Message);
            }

            TaskbarLayout? layout = TaskbarLocator.Find(synchronousAutomation: true);
            if (layout != null)
            {
                o.WriteLine($"Layout: tray left {layout.TrayLeft} from {layout.TraySource}, horizontal {layout.IsHorizontal}, " +
                    $"top {layout.IsAtTop}, client height {layout.ClientHeight}");
            }

            IntPtr label = Native.FindWindowEx(taskbar, IntPtr.Zero, null, TaskbarLabel.Caption);
            if (label != IntPtr.Zero && Native.GetWindowRect(label, out Native.RECT labelRect))
            {
                o.WriteLine($"Running label 0x{label.ToInt64():X} {labelRect} visible={Native.IsWindowVisible(label)}");
            }
            else
            {
                o.WriteLine("Running label: none");
            }

            var (target, usedFallback) = CountdownStore.Load();
            o.WriteLine($"Saved event: \"{target.Name}\" at {target.WhenUtc:o} (fallback {usedFallback}), " +
                $"start with Windows {Startup.IsEnabled()}");
        }
    }
}
