using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Countdown
{
    /// <summary>
    /// The app: ticks once a second on the whole-second boundary, keeps the taskbar label (or,
    /// when the taskbar cannot host it, a notification-area icon) up to date, and runs the menu.
    /// </summary>
    internal sealed class CountdownApp : ApplicationContext
    {
        const uint IdName = 10, IdDate = 11, IdRemaining = 12;
        const uint IdSetCountdown = 1, IdStartWithWindows = 2, IdQuit = 3;

        readonly MessageWindow messages = new MessageWindow();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly TaskbarLabel label = new TaskbarLabel();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly Icon appIcon = AppIcon.Load();
        SettingsForm? settings;
        CountdownTarget target;
        bool lightTaskbar;
        bool menuOpen;

        public CountdownApp()
        {
            var (loaded, usedFallback) = CountdownStore.Load();
            target = loaded;

            tray.Icon = AppIcon.LoadSmall();
            tray.Text = "Countdown";
            tray.MouseUp += (s, e) => ShowMenu(anchorToLabel: false);
            label.Clicked += () => ShowMenu(anchorToLabel: true);
            messages.ShowSettingsRequested += ShowSettings;
            messages.ThemeChanged += Resync;

            lightTaskbar = Theme.TaskbarIsLight();
            Theme.MatchMenusToTaskbar();

            // System events can arrive on another thread; hop to the UI thread first.
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.TimeChanged += OnSystemChanged;
            SystemEvents.DisplaySettingsChanged += OnSystemChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            timer.Tick += (s, e) => Resync();
            Resync();

            // Nothing saved yet: show the default countdown and ask for the person's own event.
            if (usedFallback) ShowSettings();
        }

        // MARK: ticking

        /// <summary>Refreshes everything now and arms the timer for the next whole second.</summary>
        void Resync()
        {
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                Log.Write("tick failed: " + e);
            }
            finally
            {
                ScheduleNextTick();
            }
        }

        /// <summary>
        /// One-shot timers, each armed for 20 ms past the next whole-second boundary and re-armed
        /// from the wall clock on every tick, so timer drift never accumulates. The margin covers
        /// the 15.6 ms granularity of both the timer and DateTime.UtcNow on .NET Framework, so a
        /// tick never reads the second before the boundary it was meant for.
        /// </summary>
        void ScheduleNextTick()
        {
            timer.Stop();
            timer.Interval = Math.Max(1, 1000 - DateTime.UtcNow.Millisecond + 20);
            timer.Start();
        }

        void Tick()
        {
            bool light = Theme.TaskbarIsLight();
            if (light != lightTaskbar)
            {
                lightTaskbar = light;
                Theme.MatchMenusToTaskbar();
            }

            double remaining = Clock.SecondsUntil(target.WhenUtc);
            string title = CountdownFormatter.TaskbarTitle(remaining, TaskbarLabel.Shorten(target.Name));
            bool onTaskbar = false;
            TaskbarLayout? layout = TaskbarLocator.Find();
            if (layout != null && layout.IsHorizontal)
            {
                onTaskbar = label.Update(title, CountdownFormatter.TaskbarCompact(remaining), layout, light);
            }
            else
            {
                label.Detach();
            }

            // The notification-area icon is only a fallback: no taskbar, a vertical one, or one
            // too crowded for even the compact text.
            if (!onTaskbar) tray.Text = Truncate(title, 63);
            if (tray.Visible == onTaskbar) tray.Visible = !onTaskbar;

            settings?.Tick(light);
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) ResyncOnUiThread();
        }

        void OnSystemChanged(object? sender, EventArgs e) => ResyncOnUiThread();

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.VisualStyle) ResyncOnUiThread();
        }

        void ResyncOnUiThread()
        {
            if (messages.IsDisposed) return;
            try
            {
                messages.BeginInvoke((Action)Resync);
            }
            catch (InvalidOperationException)
            {
                // The window is going away with the app.
            }
        }

        // MARK: menu

        void ShowMenu(bool anchorToLabel)
        {
            if (menuOpen) return;
            menuOpen = true;
            IntPtr menu = Native.CreatePopupMenu();
            try
            {
                DateTime local = target.WhenUtc.ToLocalTime();
                Add(menu, Native.MF_STRING | Native.MF_DISABLED, IdName, MenuText(target.Name));
                Add(menu, Native.MF_STRING | Native.MF_GRAYED, IdDate, local.ToString("D") + " at " + local.ToString("t"));
                Add(menu, Native.MF_STRING | Native.MF_GRAYED, IdRemaining, CountdownFormatter.FullRemaining(Clock.SecondsUntil(target.WhenUtc)));
                Native.SetMenuDefaultItem(menu, IdName, 0); // bold, like the macOS header row
                Native.AppendMenu(menu, Native.MF_SEPARATOR, UIntPtr.Zero, null);
                Add(menu, Native.MF_STRING, IdSetCountdown, "Set Countdown…");
                Add(menu, Native.MF_STRING | (Startup.IsEnabled() ? Native.MF_CHECKED : 0), IdStartWithWindows, "Start with Windows");
                Native.AppendMenu(menu, Native.MF_SEPARATOR, UIntPtr.Zero, null);
                Add(menu, Native.MF_STRING, IdQuit, "Quit Countdown");

                uint flags = Native.TPM_RETURNCMD | Native.TPM_NONOTIFY | Native.TPM_RIGHTBUTTON;
                int x, y;
                TaskbarLayout? layout = label.Layout;
                if (anchorToLabel && layout != null && label.TryGetScreenRect(out Native.RECT rect))
                {
                    // Centred on the label, opening away from the taskbar.
                    x = (rect.Left + rect.Right) / 2;
                    y = layout.IsAtTop ? layout.Bounds.Bottom : layout.Bounds.Top;
                    flags |= Native.TPM_CENTERALIGN | (layout.IsAtTop ? Native.TPM_TOPALIGN : Native.TPM_BOTTOMALIGN);
                }
                else
                {
                    Native.GetCursorPos(out Native.POINT cursor);
                    x = cursor.X;
                    y = cursor.Y;
                    flags |= Native.TPM_RIGHTALIGN | Native.TPM_BOTTOMALIGN;
                }

                // The classic notification-area dance: be foreground so the menu closes on an
                // outside click, then post a null message so it does not reopen on the next one.
                Native.SetForegroundWindow(messages.Handle);
                int command = Native.TrackPopupMenuEx(menu, flags, x, y, messages.Handle, IntPtr.Zero);
                Native.PostMessage(messages.Handle, Native.WM_NULL, IntPtr.Zero, IntPtr.Zero);

                switch ((uint)command)
                {
                    case IdSetCountdown: ShowSettings(); break;
                    case IdStartWithWindows: ToggleStartWithWindows(); break;
                    case IdQuit: ExitThread(); break;
                }
            }
            finally
            {
                Native.DestroyMenu(menu);
                menuOpen = false;
            }
        }

        static void Add(IntPtr menu, uint flags, uint id, string text) =>
            Native.AppendMenu(menu, flags, (UIntPtr)id, text);

        /// <summary>Menus treat "&amp;" as a mnemonic marker and a tab as a column break.</summary>
        static string MenuText(string text) => text.Replace("&", "&&").Replace('\t', ' ');

        static string Truncate(string text, int max) =>
            text.Length <= max ? text : text.Substring(0, max - 1) + "…";

        void ToggleStartWithWindows()
        {
            try
            {
                Startup.SetEnabled(!Startup.IsEnabled());
            }
            catch (Exception e)
            {
                MessageBox.Show("Countdown couldn't change Start with Windows.\n\n" + e.Message,
                    "Countdown", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // MARK: settings

        void ShowSettings()
        {
            if (settings == null || settings.IsDisposed)
            {
                settings = new SettingsForm(appIcon);
                settings.Saved += Apply;
            }
            settings.ShowFor(target, lightTaskbar);
        }

        void Apply(CountdownTarget newTarget)
        {
            target = newTarget;
            try
            {
                CountdownStore.Save(newTarget);
            }
            catch (Exception e)
            {
                MessageBox.Show("Countdown couldn't save your event.\n\n" + e.Message,
                    "Countdown", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            Resync();
        }

        // MARK: shutdown

        protected override void ExitThreadCore()
        {
            timer.Stop();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.TimeChanged -= OnSystemChanged;
            SystemEvents.DisplaySettingsChanged -= OnSystemChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            tray.Visible = false;
            label.Detach();
            if (settings != null)
            {
                settings.AllowClose();
                settings.Close();
            }
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                tray.Dispose();
                label.Dispose();
                settings?.Dispose();
                messages.Dispose();
                appIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
