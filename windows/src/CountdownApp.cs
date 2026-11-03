using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Countdown
{
    /// <summary>
    /// The app: ticks once a second on the whole-second boundary, keeps the taskbar label (or,
    /// when the taskbar has no room for it, a notification-area icon) up to date, and runs the menu.
    /// </summary>
    internal sealed class CountdownApp : ApplicationContext
    {
        const uint IdName = 10, IdDate = 11, IdRemaining = 12;
        const uint IdSetCountdown = 1, IdStartWithWindows = 2, IdQuit = 3;

        /// <summary>The longest tray tooltip .NET Framework accepts.</summary>
        const int MaxTooltipLength = 63;

        readonly MessageWindow messages = new MessageWindow();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly TaskbarLabel label = new TaskbarLabel();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly Icon appIcon = AppIcon.Load();
        SettingsForm? settings;
        CountdownTarget target;
        bool lightTaskbar;
        bool menuOpen;
        int menuClosedAt = Environment.TickCount - 60_000;

        public CountdownApp()
        {
            var (loaded, usedFallback) = CountdownStore.Load();
            target = loaded;

            tray.Icon = AppIcon.LoadSmall();
            tray.Text = "Countdown";
            tray.MouseUp += OnTrayMouseUp;
            label.Clicked += () => ShowMenu(anchorToLabel: true);
            messages.ShowSettingsRequested += ShowSettings;
            messages.ThemeChanged += Resync;

            lightTaskbar = Theme.TaskbarIsLight();
            Theme.MatchMenusToTaskbar();

            // System events and scans arrive on other threads; they hop to the UI thread first.
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.TimeChanged += OnTimeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            TaskbarScan.Changed += ResyncOnUiThread;

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
            LabelState state = LabelState.Unavailable;
            TaskbarLayout? layout = TaskbarLocator.Find();
            if (layout != null && layout.IsHorizontal)
            {
                state = label.Update(title, CountdownFormatter.TaskbarCompact(remaining), layout, light);
            }
            else
            {
                label.Detach();
            }

            // The notification-area icon is only a fallback: no taskbar, a vertical one, or one too
            // crowded for even the time alone. While the first scan runs, neither is shown.
            bool useTray = state == LabelState.NoRoom || state == LabelState.Unavailable;
            if (useTray) tray.Text = CountdownFormatter.Ellipsize(title, MaxTooltipLength);
            if (tray.Visible != useTray) tray.Visible = useTray;

            settings?.Tick(light);
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) ResyncOnUiThread();
        }

        void OnTimeChanged(object? sender, EventArgs e)
        {
            // .NET Framework caches the local time zone; a zone change also changes the time.
            TimeZoneInfo.ClearCachedData();
            ResyncOnUiThread();
        }

        void OnDisplaySettingsChanged(object? sender, EventArgs e) => ResyncOnUiThread();

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.Locale) CultureInfo.CurrentCulture.ClearCachedData();
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.VisualStyle
                || e.Category == UserPreferenceCategory.Locale)
            {
                ResyncOnUiThread();
            }
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

        void OnTrayMouseUp(object? sender, MouseEventArgs e)
        {
            // The click on the icon that closed the menu must not open it again.
            if (unchecked(Environment.TickCount - menuClosedAt) < 300) return;
            ShowMenu(anchorToLabel: false);
        }

        void ShowMenu(bool anchorToLabel)
        {
            if (menuOpen) return;
            menuOpen = true;
            TimeZoneInfo.ClearCachedData();
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
                menuClosedAt = Environment.TickCount;
                label.NoteMenuClosed();

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
            TimeZoneInfo.ClearCachedData();
            // WinForms on .NET Framework cannot rescale a form for another monitor's DPI, so the
            // settings window is system-aware and Windows scales it instead. Every handle it
            // owns is created inside this scope (on construction and on its first Show).
            IntPtr previousDpi = Native.EnterSystemAwareDpi();
            try
            {
                if (settings == null || settings.IsDisposed)
                {
                    settings = new SettingsForm(appIcon);
                    settings.Saved += Apply;
                }
                settings.ShowFor(target, lightTaskbar);
            }
            finally
            {
                Native.RestoreDpi(previousDpi);
            }
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
            SystemEvents.TimeChanged -= OnTimeChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            TaskbarScan.Changed -= ResyncOnUiThread;
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
