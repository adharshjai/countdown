using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Countdown
{
    /// <summary>
    /// The countdown text, drawn inside the taskbar.
    ///
    /// It is a layered child window of the taskbar (Shell_TrayWnd): it moves, hides and
    /// auto-hides with the taskbar, and per-pixel alpha lets the taskbar's own background show
    /// through. Layered child windows need the Windows 8+ entries in app.manifest. If Explorer
    /// restarts, the next tick notices the new taskbar and builds the window again.
    /// </summary>
    internal enum LabelState
    {
        /// <summary>On the taskbar.</summary>
        Shown,
        /// <summary>Waiting for the first scan of the taskbar's buttons; shows nothing yet.</summary>
        NotReady,
        /// <summary>Even the time alone does not fit beside the taskbar's buttons.</summary>
        NoRoom,
        /// <summary>The taskbar cannot host the label.</summary>
        Unavailable,
    }

    internal sealed class TaskbarLabel : NativeWindow, IDisposable
    {
        public const string Caption = "CountdownTaskbarLabel";

        /// <summary>The longest event name the taskbar shows before cutting it with an ellipsis.</summary>
        const int MaxNameLength = 48;

        const int SW_HIDE = 0;

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public event Action? Clicked;

        TaskbarLayout? layout;
        string fullText = "";
        string compactText = "";
        int textRgb = -1;
        int dpi;
        int height;
        bool hover;
        bool renderedHover;
        bool pressed;
        bool menuJustClosed;
        int menuClosedAt;
        Raster? full;
        Raster? compact;
        Raster? shown;
        Native.RECT placed;

        public TaskbarLayout? Layout => layout;

        public static string Shorten(string name) => CountdownFormatter.Ellipsize(name, MaxNameLength);

        /// <summary>
        /// Called when the menu closes. A click on the label that closes the menu has its
        /// button-down eaten by the menu, so only presses that start after this open it again.
        /// </summary>
        public void NoteMenuClosed()
        {
            menuClosedAt = Environment.TickCount;
            menuJustClosed = true;
            pressed = false;
        }

        /// <summary>
        /// Shows the full text where it fits, else the compact text. Anything but Shown leaves
        /// the label hidden; NoRoom and Unavailable tell the caller to use the tray icon.
        /// </summary>
        public LabelState Update(string newFull, string newCompact, TaskbarLayout newLayout, bool lightTaskbar)
        {
            if (!EnsureWindow(newLayout.Taskbar)) return LabelState.Unavailable;
            layout = newLayout;
            if (newLayout.Occupied == null)
            {
                // Until the first scan says where the buttons are, any spot could cover one.
                Conceal();
                return LabelState.NotReady;
            }

            int newRgb = lightTaskbar ? 0x000000 : 0xFFFFFF;
            if (newRgb != textRgb || newLayout.Dpi != dpi || newLayout.ClientHeight != height || hover != renderedHover)
            {
                textRgb = newRgb;
                dpi = newLayout.Dpi;
                height = newLayout.ClientHeight;
                renderedHover = hover;
                full = null;
                compact = null;
            }
            if (full == null || newFull != fullText)
            {
                fullText = newFull;
                full = TextRaster.Render(fullText, StyleFor(lightTaskbar));
            }
            if (compact == null || newCompact != compactText)
            {
                compactText = newCompact;
                compact = TextRaster.Render(compactText, StyleFor(lightTaskbar));
            }

            int margin = Native.Scale(4, dpi);
            int areaLeft = newLayout.Bounds.Left + margin;
            int areaRight = newLayout.TrayLeft - margin;
            Raster chosen = full;
            int? screenX = Placement.Choose(full.Width, areaLeft, areaRight, newLayout.Occupied, margin);
            if (screenX == null)
            {
                chosen = compact;
                screenX = Placement.Choose(compact.Width, areaLeft, areaRight, newLayout.Occupied, margin);
            }
            if (screenX == null)
            {
                Conceal();
                return LabelState.NoRoom;
            }

            var origin = new Native.POINT(screenX.Value, newLayout.Bounds.Top);
            Native.ScreenToClient(newLayout.Taskbar, ref origin);
            var target = new Native.RECT { Left = origin.X, Top = 0, Right = origin.X + chosen.Width, Bottom = chosen.Height };
            if (!ReferenceEquals(chosen, shown) || !SameRect(target, placed))
            {
                Native.SetWindowPos(Handle, Native.HWND_TOP, target.Left, target.Top, target.Width, target.Height,
                    Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
                Upload(chosen);
                shown = chosen;
                placed = target;
            }
            else
            {
                // Explorer reorders its own children now and then; stay on top of them.
                Native.SetWindowPos(Handle, Native.HWND_TOP, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            }
            return LabelState.Shown;
        }

        /// <summary>The label in screen pixels, for anchoring the menu.</summary>
        public bool TryGetScreenRect(out Native.RECT rect)
        {
            rect = default;
            return Handle != IntPtr.Zero && shown != null && Native.GetWindowRect(Handle, out rect);
        }

        public void Detach()
        {
            if (Handle != IntPtr.Zero)
            {
                if (Native.IsWindow(Handle)) DestroyHandle();
                else ReleaseHandle();
            }
            full = null;
            compact = null;
            shown = null;
        }

        public void Dispose() => Detach();

        /// <summary>Hides the window but keeps it, for when nothing fits on a crowded taskbar.</summary>
        void Conceal()
        {
            if (Handle != IntPtr.Zero && shown != null) ShowWindow(Handle, SW_HIDE);
            shown = null;
        }

        TextRaster.Style StyleFor(bool lightTaskbar) => new TextRaster.Style
        {
            TextRgb = lightTaskbar ? 0x000000 : 0xFFFFFF,
            Dpi = dpi,
            Height = height,
            PadX = Native.Scale(8, dpi),
            // The hover highlight the taskbar clock uses: a faint rounded rectangle.
            BackgroundArgb = !renderedHover ? 0u : lightTaskbar ? 0x10000000u : 0x18FFFFFFu,
            BackgroundInsetY = Native.Scale(4, dpi),
            BackgroundRadius = Native.Scale(4, dpi),
            Clickable = true,
        };

        bool EnsureWindow(IntPtr taskbar)
        {
            if (Handle != IntPtr.Zero)
            {
                if (Native.IsWindow(Handle) && Native.GetAncestor(Handle, Native.GA_PARENT) == taskbar) return true;
                Log.Write("label: taskbar changed, rebuilding");
                Detach();
            }

            var cp = new CreateParams
            {
                Caption = Caption,
                Parent = taskbar,
                Style = Native.WS_CHILD | Native.WS_CLIPSIBLINGS,
                ExStyle = Native.WS_EX_LAYERED,
                X = 0,
                Y = 0,
                Width = 1,
                Height = 1,
            };
            try
            {
                CreateHandle(cp);
            }
            catch (Exception e)
            {
                Log.Write("label: create failed: " + e.Message);
                return false;
            }
            full = null;
            compact = null;
            shown = null;
            Log.Write($"label: created 0x{Handle.ToInt64():X} in taskbar 0x{taskbar.ToInt64():X}");
            return Handle != IntPtr.Zero;
        }

        void Upload(Raster r)
        {
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            IntPtr dc = Native.CreateCompatibleDC(screen);
            var info = new Native.BITMAPINFO();
            info.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
            info.bmiHeader.biWidth = r.Width;
            info.bmiHeader.biHeight = -r.Height;
            info.bmiHeader.biPlanes = 1;
            info.bmiHeader.biBitCount = 32;
            IntPtr dib = Native.CreateDIBSection(dc, ref info, 0, out IntPtr bits, IntPtr.Zero, 0);
            IntPtr previous = IntPtr.Zero;
            try
            {
                if (dib == IntPtr.Zero) return;
                Marshal.Copy(r.Pixels, 0, bits, r.Pixels.Length);
                previous = Native.SelectObject(dc, dib);
                var size = new Native.SIZE(r.Width, r.Height);
                var source = new Native.POINT(0, 0);
                var blend = new Native.BLENDFUNCTION
                {
                    BlendOp = Native.AC_SRC_OVER,
                    SourceConstantAlpha = 255,
                    AlphaFormat = Native.AC_SRC_ALPHA,
                };
                if (!Native.UpdateLayeredWindow(Handle, screen, IntPtr.Zero, ref size, dc, ref source, 0, ref blend, Native.ULW_ALPHA))
                {
                    Log.Write("label: UpdateLayeredWindow failed, error " + Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                if (previous != IntPtr.Zero) Native.SelectObject(dc, previous);
                if (dib != IntPtr.Zero) Native.DeleteObject(dib);
                Native.DeleteDC(dc);
                Native.ReleaseDC(IntPtr.Zero, screen);
            }
        }

        void Redraw()
        {
            if (layout == null || shown == null) return;
            full = null; // forces a render with the new hover state
            compact = null;
            Update(fullText, compactText, layout, textRgb == 0x000000);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case Native.WM_MOUSEACTIVATE:
                    m.Result = (IntPtr)Native.MA_NOACTIVATE;
                    return;
                case Native.WM_MOUSEMOVE:
                    if (!hover)
                    {
                        hover = true;
                        var track = new Native.TRACKMOUSEEVENT
                        {
                            cbSize = (uint)Marshal.SizeOf(typeof(Native.TRACKMOUSEEVENT)),
                            dwFlags = Native.TME_LEAVE,
                            hwndTrack = Handle,
                        };
                        Native.TrackMouseEvent(ref track);
                        Redraw();
                    }
                    break;
                case Native.WM_MOUSELEAVE:
                    hover = false;
                    Redraw();
                    break;
                case Native.WM_LBUTTONDOWN:
                case Native.WM_RBUTTONDOWN:
                    // The first press after the menu closed counts only if it came after the close
                    // (the press that closed it may still be queued); later presses always count.
                    pressed = !menuJustClosed || unchecked(Native.GetMessageTime() - menuClosedAt) > 0;
                    menuJustClosed = false;
                    return;
                case Native.WM_LBUTTONUP:
                case Native.WM_RBUTTONUP:
                    menuJustClosed = false;
                    if (pressed)
                    {
                        pressed = false;
                        Clicked?.Invoke();
                    }
                    return;
            }
            base.WndProc(ref m);
        }

        static bool SameRect(Native.RECT a, Native.RECT b) =>
            a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
    }
}
