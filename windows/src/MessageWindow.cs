using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Countdown
{
    /// <summary>
    /// A hidden top-level window: it owns the popup menu (so the menu closes when you click
    /// elsewhere), hears theme changes, and takes the "show settings" message a second launch
    /// sends to the running copy.
    /// </summary>
    internal sealed class MessageWindow : Form
    {
        public const string WindowTitle = "Countdown.MessageWindow.6e1b0d47";
        public static readonly int ShowSettingsMessage = (int)Native.RegisterWindowMessage("Countdown.ShowSettings.6e1b0d47");

        public event Action? ShowSettingsRequested;
        public event Action? ThemeChanged;

        public MessageWindow()
        {
            Text = WindowTitle;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = new System.Drawing.Point(-32000, -32000);
            Size = new System.Drawing.Size(1, 1);
            _ = Handle; // create the window now, without showing it
            // Let a second launch reach this window even if this copy runs as administrator.
            if (ShowSettingsMessage != 0) Native.ChangeWindowMessageFilterEx(Handle, (uint)ShowSettingsMessage, Native.MSGFLT_ALLOW, IntPtr.Zero);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);

        protected override void WndProc(ref Message m)
        {
            if (ShowSettingsMessage != 0 && m.Msg == ShowSettingsMessage)
            {
                ShowSettingsRequested?.Invoke();
                return;
            }
            if (m.Msg == Native.WM_SETTINGCHANGE && m.LParam != IntPtr.Zero
                && Marshal.PtrToStringUni(m.LParam) == "ImmersiveColorSet")
            {
                ThemeChanged?.Invoke();
            }
            base.WndProc(ref m);
        }
    }
}
