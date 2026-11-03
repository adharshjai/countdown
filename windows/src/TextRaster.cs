using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace Countdown
{
    /// <summary>Premultiplied BGRA pixels, top row first, ready for UpdateLayeredWindow.</summary>
    internal sealed class Raster
    {
        public Raster(int width, int height, int[] pixels)
        {
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public int Width { get; }
        public int Height { get; }
        public int[] Pixels { get; }

        public Bitmap ToBitmap()
        {
            var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                for (int y = 0; y < Height; y++)
                {
                    Marshal.Copy(Pixels, y * Width, data.Scan0 + y * data.Stride, Width);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }
    }

    /// <summary>
    /// Draws taskbar text with a transparent background. GDI cannot write alpha, so the text is
    /// drawn white on black with grayscale antialiasing and that coverage becomes the alpha of
    /// the real text colour. Emoji are drawn in Segoe UI Emoji, the rest in Segoe UI.
    /// </summary>
    internal static class TextRaster
    {
        public const string TextFace = "Segoe UI";
        public const string EmojiFace = "Segoe UI Emoji";

        /// <summary>The taskbar clock's size: 9 pt at the taskbar's DPI.</summary>
        public const double PointSize = 9.0;

        public sealed class Style
        {
            public int TextRgb = 0xFFFFFF;
            public int Dpi = 96;
            /// <summary>Total height; 0 sizes the raster to the text.</summary>
            public int Height;
            public int PadX;
            /// <summary>Straight ARGB of a rounded background, 0 for none (the hover state).</summary>
            public uint BackgroundArgb;
            public int BackgroundInsetY;
            public int BackgroundRadius;
            /// <summary>Gives fully transparent pixels an alpha of 1 so the whole rectangle takes clicks.</summary>
            public bool Clickable;
        }

        public static Raster Render(string text, Style style)
        {
            var runs = SplitRuns(text);
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            IntPtr dc = Native.CreateCompatibleDC(screen);
            Native.ReleaseDC(IntPtr.Zero, screen);
            IntPtr textFont = CreateFont(TextFace, style.Dpi);
            IntPtr emojiFont = CreateFont(EmojiFace, style.Dpi);
            IntPtr previousFont = Native.SelectObject(dc, textFont);
            IntPtr dib = IntPtr.Zero;
            IntPtr previousBitmap = IntPtr.Zero;
            try
            {
                var widths = new int[runs.Count];
                var heights = new int[runs.Count];
                int textWidth = 0;
                int textHeight = 0;
                for (int i = 0; i < runs.Count; i++)
                {
                    Native.SelectObject(dc, runs[i].emoji ? emojiFont : textFont);
                    var measured = new Native.RECT();
                    Native.DrawText(dc, runs[i].text, runs[i].text.Length, ref measured,
                        Native.DT_CALCRECT | Native.DT_SINGLELINE | Native.DT_NOPREFIX);
                    widths[i] = measured.Width;
                    heights[i] = measured.Height;
                    textWidth += measured.Width;
                    textHeight = Math.Max(textHeight, measured.Height);
                }
                if (runs.Count == 0)
                {
                    Native.SelectObject(dc, textFont);
                    var measured = new Native.RECT();
                    Native.DrawText(dc, " ", 1, ref measured, Native.DT_CALCRECT | Native.DT_SINGLELINE | Native.DT_NOPREFIX);
                    textHeight = measured.Height;
                }

                int width = Math.Max(1, textWidth + 2 * style.PadX);
                int height = Math.Max(1, style.Height > 0 ? style.Height : textHeight);

                var info = new Native.BITMAPINFO();
                info.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
                info.bmiHeader.biWidth = width;
                info.bmiHeader.biHeight = -height; // top-down rows
                info.bmiHeader.biPlanes = 1;
                info.bmiHeader.biBitCount = 32;
                dib = Native.CreateDIBSection(dc, ref info, 0, out IntPtr bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero) throw new Win32Exception("CreateDIBSection failed");
                previousBitmap = Native.SelectObject(dc, dib);

                var pixels = new int[width * height];
                Marshal.Copy(pixels, 0, bits, pixels.Length); // black
                Native.SetBkMode(dc, Native.TRANSPARENT);
                Native.SetTextColor(dc, 0x00FFFFFF);

                int x = style.PadX;
                for (int i = 0; i < runs.Count; i++)
                {
                    Native.SelectObject(dc, runs[i].emoji ? emojiFont : textFont);
                    int y = (height - heights[i]) / 2;
                    var box = new Native.RECT { Left = x, Top = y, Right = x + widths[i], Bottom = y + heights[i] };
                    Native.DrawText(dc, runs[i].text, runs[i].text.Length, ref box,
                        Native.DT_SINGLELINE | Native.DT_NOPREFIX | Native.DT_NOCLIP);
                    x += widths[i];
                }
                Native.GdiFlush();
                Marshal.Copy(bits, pixels, 0, pixels.Length);
                Compose(pixels, width, height, style);
                return new Raster(width, height, pixels);
            }
            finally
            {
                Native.SelectObject(dc, previousFont);
                if (previousBitmap != IntPtr.Zero) Native.SelectObject(dc, previousBitmap);
                if (dib != IntPtr.Zero) Native.DeleteObject(dib);
                Native.DeleteObject(textFont);
                Native.DeleteObject(emojiFont);
                Native.DeleteDC(dc);
            }
        }

        /// <summary>
        /// Splits text into runs for the two fonts. Surrogate pairs and the BMP symbol blocks
        /// are emoji; joiners, variation selectors and keycaps stay with the run they follow.
        /// </summary>
        public static List<(string text, bool emoji)> SplitRuns(string text)
        {
            var runs = new List<(string text, bool emoji)>();
            var current = new StringBuilder();
            bool currentEmoji = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool isPair = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
                bool isEmoji;
                if (isPair)
                {
                    isEmoji = true;
                }
                else if (c == '‍' || c == '️' || c == '⃣')
                {
                    isEmoji = current.Length > 0 && currentEmoji;
                }
                else
                {
                    isEmoji = (c >= '⌀' && c <= '⏿') || (c >= '☀' && c <= '➿') || (c >= '⬀' && c <= '⯿');
                }

                if (current.Length > 0 && isEmoji != currentEmoji)
                {
                    runs.Add((current.ToString(), currentEmoji));
                    current.Clear();
                }
                currentEmoji = isEmoji;
                current.Append(c);
                if (isPair)
                {
                    current.Append(text[i + 1]);
                    i++;
                }
            }
            if (current.Length > 0) runs.Add((current.ToString(), currentEmoji));
            return runs;
        }

        static IntPtr CreateFont(string face, int dpi)
        {
            var font = new Native.LOGFONT
            {
                lfHeight = -(int)Math.Round(PointSize * dpi / 72.0),
                lfWeight = Native.FW_NORMAL,
                lfCharSet = Native.DEFAULT_CHARSET,
                lfQuality = Native.ANTIALIASED_QUALITY,
                lfFaceName = face,
            };
            return Native.CreateFontIndirect(ref font);
        }

        /// <summary>
        /// Turns white-on-black coverage into premultiplied text colour over the optional
        /// rounded background: out = text + background * (1 - text alpha).
        /// </summary>
        static void Compose(int[] pixels, int width, int height, Style style)
        {
            int tr = (style.TextRgb >> 16) & 0xFF, tg = (style.TextRgb >> 8) & 0xFF, tb = style.TextRgb & 0xFF;
            int ba = (int)((style.BackgroundArgb >> 24) & 0xFF);
            int br = (int)((style.BackgroundArgb >> 16) & 0xFF), bg = (int)((style.BackgroundArgb >> 8) & 0xFF), bb = (int)(style.BackgroundArgb & 0xFF);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    int p = pixels[i];
                    int coverage = ((p & 0xFF) + ((p >> 8) & 0xFF) + ((p >> 16) & 0xFF)) / 3;
                    int backA = ba == 0 ? 0 : ba * RoundRectCoverage(x, y, width, height, style.BackgroundInsetY, style.BackgroundRadius) / 255;
                    int backR = br * backA / 255, backG = bg * backA / 255, backB = bb * backA / 255;
                    int rest = 255 - coverage;
                    int a = coverage + backA * rest / 255;
                    int r = tr * coverage / 255 + backR * rest / 255;
                    int g = tg * coverage / 255 + backG * rest / 255;
                    int b = tb * coverage / 255 + backB * rest / 255;
                    if (style.Clickable && a == 0) a = 1;
                    pixels[i] = (a << 24) | (r << 16) | (g << 8) | b;
                }
            }
        }

        /// <summary>Antialiased coverage (0-255) of a pixel by a rounded rectangle inset vertically.</summary>
        static int RoundRectCoverage(int x, int y, int width, int height, int insetY, int radius)
        {
            double top = insetY, bottom = height - insetY;
            if (bottom <= top) return 0;
            double halfW = width / 2.0, halfH = (bottom - top) / 2.0;
            double r = Math.Min(radius, Math.Min(halfW, halfH));
            double qx = Math.Abs(x + 0.5 - halfW) - (halfW - r);
            double qy = Math.Abs(y + 0.5 - (top + halfH)) - (halfH - r);
            double outside = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0));
            double inside = Math.Min(Math.Max(qx, qy), 0);
            double distance = outside + inside - r;
            double coverage = Math.Max(0, Math.Min(1, 0.5 - distance));
            return (int)(coverage * 255 + 0.5);
        }
    }
}
