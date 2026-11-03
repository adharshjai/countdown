using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Countdown
{
    /// <summary>
    /// "Set Countdown": a live preview of the taskbar text, the event name, a calendar and a
    /// time field, Save / Cancel. Built once and kept; <see cref="ShowFor"/> repopulates it.
    /// Enter saves, Esc cancels.
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        readonly PreviewBar preview = new PreviewBar();
        readonly TextBox nameBox = new TextBox();
        readonly MonthCalendar calendar = new MonthCalendar();
        readonly DateTimePicker timePicker = new DateTimePicker();
        readonly Button saveButton = new Button();
        readonly Button cancelButton = new Button();
        bool lightTaskbar;
        bool allowClose;

        public event Action<CountdownTarget>? Saved;

        public SettingsForm(Icon icon)
        {
            SuspendLayout();
            Text = "Set Countdown";
            Icon = icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Segoe UI", 9F);
            BackColor = SystemColors.Window;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(20, 20, 20, 16),
                Location = new Point(0, 0),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            preview.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            preview.Height = 40;
            preview.Margin = new Padding(0, 0, 0, 16);
            layout.Controls.Add(preview);

            layout.Controls.Add(Caption("Event"));
            nameBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            nameBox.MinimumSize = new Size(300, 0);
            nameBox.Margin = new Padding(0, 4, 0, 14);
            nameBox.HandleCreated += (s, e) => Native.SendMessage(nameBox.Handle, Native.EM_SETCUEBANNER, (IntPtr)1, "Trip to Japan");
            layout.Controls.Add(nameBox);

            layout.Controls.Add(Caption("Date"));
            calendar.MaxSelectionCount = 1;
            calendar.Anchor = AnchorStyles.Left;
            calendar.Margin = new Padding(0, 4, 0, 14);
            layout.Controls.Add(calendar);

            layout.Controls.Add(Caption("Time"));
            timePicker.Format = DateTimePickerFormat.Custom;
            timePicker.CustomFormat = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern;
            timePicker.ShowUpDown = true;
            timePicker.Width = 120;
            timePicker.Anchor = AnchorStyles.Left;
            timePicker.Margin = new Padding(0, 4, 0, 20);
            layout.Controls.Add(timePicker);

            // Windows order: the action first, Cancel at the right edge.
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0),
            };
            cancelButton.Text = "Cancel";
            saveButton.Text = "Save";
            foreach (var button in new[] { cancelButton, saveButton })
            {
                button.AutoSize = true;
                button.MinimumSize = new Size(88, 30);
                button.Margin = new Padding(8, 0, 0, 0);
                buttons.Controls.Add(button);
            }
            saveButton.TabIndex = 0;
            cancelButton.TabIndex = 1;
            layout.Controls.Add(buttons);
            Controls.Add(layout);

            AcceptButton = saveButton;
            CancelButton = cancelButton;
            saveButton.Click += (s, e) => Save();
            cancelButton.Click += (s, e) => Hide();
            nameBox.TextChanged += (s, e) => { saveButton.Enabled = NameText.Length > 0; RefreshPreview(); };
            calendar.DateChanged += (s, e) => RefreshPreview();
            timePicker.ValueChanged += (s, e) => RefreshPreview();
            ResumeLayout(true);
            calendar.CalendarDimensions = new Size(1, 1);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            calendar.CalendarDimensions = new Size(1, 1);
        }

        string NameText => nameBox.Text.Trim();

        /// <summary>The picked date and time as a local wall-clock time.</summary>
        DateTime SelectedLocal
        {
            get
            {
                DateTime day = calendar.SelectionStart.Date;
                DateTime time = timePicker.Value;
                return DateTime.SpecifyKind(day.AddHours(time.Hour).AddMinutes(time.Minute), DateTimeKind.Local);
            }
        }

        /// <summary>Fills the form from <paramref name="target"/> and brings it to the front.</summary>
        public void ShowFor(CountdownTarget target, bool taskbarIsLight)
        {
            lightTaskbar = taskbarIsLight;
            DateTime local = target.WhenUtc.ToLocalTime();
            if (local < calendar.MinDate) local = calendar.MinDate;
            if (local > calendar.MaxDate) local = calendar.MaxDate;
            nameBox.Text = target.Name;
            calendar.SetDate(local.Date);
            timePicker.Value = new DateTime(2000, 1, 1, local.Hour, local.Minute, 0);
            saveButton.Enabled = NameText.Length > 0;
            RefreshPreview();

            if (!Visible)
            {
                CenterToScreen();
                Show();
            }
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            Native.SetForegroundWindow(Handle);
            nameBox.Focus();
            nameBox.SelectAll();
        }

        /// <summary>Called every second by the app so the preview counts down like the taskbar.</summary>
        public void Tick(bool taskbarIsLight)
        {
            lightTaskbar = taskbarIsLight;
            if (Visible) RefreshPreview();
        }

        public void AllowClose() => allowClose = true;

        void RefreshPreview()
        {
            string name = NameText;
            preview.Set(Clock.SecondsUntil(SelectedLocal.ToUniversalTime()),
                TaskbarLabel.Shorten(name.Length > 0 ? name : "Event name"), dimmed: name.Length == 0, light: lightTaskbar);
        }

        void Save()
        {
            string name = NameText;
            if (name.Length == 0) return;
            Saved?.Invoke(new CountdownTarget(name, SelectedLocal.ToUniversalTime()));
            Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // The close box just hides the form; the next ShowFor repopulates it.
            if (!allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnFormClosing(e);
        }

        static Label Caption(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = Color.FromArgb(0x5F, 0x5F, 0x5F),
            Margin = new Padding(0),
        };
    }

    /// <summary>
    /// A slice of taskbar that shows the text as the taskbar label draws it. A name too long
    /// for the slice is cut with an ellipsis, as the macOS preview does.
    /// </summary>
    internal sealed class PreviewBar : Control
    {
        double remaining;
        string name = "";
        bool dimmed;
        bool light;
        string shownKey = "";
        Raster? fitted;
        string fittedKey = "";

        public PreviewBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }

        public void Set(double newRemaining, string newName, bool dimmed, bool light)
        {
            string key = CountdownFormatter.TaskbarTitle(newRemaining, newName) + "|" + dimmed + "|" + light;
            if (key == shownKey) return;
            shownKey = key;
            remaining = newRemaining;
            name = newName;
            this.dimmed = dimmed;
            this.light = light;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent?.BackColor ?? SystemColors.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float radius = 8F * DeviceDpi / 96F;
            var box = new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F);
            using (GraphicsPath path = RoundedRectangle(box, radius))
            using (var fill = new SolidBrush(light ? Color.FromArgb(0xEE, 0xEE, 0xEE) : Color.FromArgb(0x20, 0x20, 0x20)))
            {
                g.FillPath(fill, path);
            }

            int pad = Native.Scale(12, DeviceDpi);
            Raster raster = Fit(Math.Max(1, Width - 2 * pad));
            using (Bitmap bitmap = raster.ToBitmap())
            {
                int x = Math.Max(pad, (Width - bitmap.Width) / 2);
                int y = Math.Max(0, (Height - bitmap.Height) / 2);
                g.DrawImage(bitmap, new Rectangle(x, y, bitmap.Width, bitmap.Height));
            }
        }

        /// <summary>The line at its full length, or with the name shortened until it fits.</summary>
        Raster Fit(int available)
        {
            string key = shownKey + "|" + available + "|" + DeviceDpi;
            if (fitted != null && key == fittedKey) return fitted;

            var style = new TextRaster.Style { TextRgb = dimmed ? 0x8A8A8A : light ? 0x000000 : 0xFFFFFF, Dpi = DeviceDpi };
            string title = CountdownFormatter.TaskbarTitle(remaining, name);
            Raster raster = TextRaster.Render(title, style);
            int max = name.Length;
            while (raster.Width > available && max > 2)
            {
                int drop = Math.Max(1, (int)Math.Ceiling((raster.Width - available) * (double)title.Length / raster.Width));
                max = Math.Max(2, max - drop);
                title = CountdownFormatter.TaskbarTitle(remaining, CountdownFormatter.Ellipsize(name, max));
                raster = TextRaster.Render(title, style);
            }
            fitted = raster;
            fittedKey = key;
            return raster;
        }

        static GraphicsPath RoundedRectangle(RectangleF r, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
