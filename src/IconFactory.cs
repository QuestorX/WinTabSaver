using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace WinTabSaver
{
    /// <summary>
    /// Generates the application icon programmatically so no external .ico file
    /// is required. The icon depicts a stylised folder with a clock overlay,
    /// representing the "save Explorer session" concept.
    /// </summary>
    public static class IconFactory
    {
        // Cache to avoid recreating on every tray-icon update
        private static Icon? _cachedIcon;

        /// <summary>
        /// Returns the application <see cref="Icon"/>, creating it on first call.
        /// </summary>
        public static Icon CreateAppIcon()
        {
            if (_cachedIcon != null) return _cachedIcon;

            _cachedIcon = BuildIcon(32);
            return _cachedIcon;
        }

        /// <summary>
        /// Returns a small (16 px) version suitable for the system tray notification area.
        /// </summary>
        public static Icon CreateTrayIcon()
        {
            return BuildIcon(16);
        }

        // -- Icon drawing -------------------------------------------------------

        /// <summary>
        /// Renders the icon at the requested pixel size and wraps it in an
        /// <see cref="Icon"/> object.
        /// </summary>
        private static Icon BuildIcon(int size)
        {
            using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode     = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);

                float s = size; // shorthand

                // -- Folder body ------------------------------------------------
                // Tab on top-left
                var tabRect = new RectangleF(s * 0.04f, s * 0.20f, s * 0.35f, s * 0.12f);
                using var folderBrush = new SolidBrush(Color.FromArgb(255, 255, 193, 7)); // amber
                g.FillRectangle(folderBrush, tabRect);

                // Main body
                var bodyRect = new RectangleF(s * 0.04f, s * 0.28f, s * 0.92f, s * 0.54f);
                g.FillRoundedRect(folderBrush, bodyRect, s * 0.08f);

                // Darker folder edge / shadow
                using var edgeBrush = new SolidBrush(Color.FromArgb(60, 0, 0, 0));
                var shadowRect = new RectangleF(bodyRect.X, bodyRect.Bottom - s * 0.06f,
                                               bodyRect.Width, s * 0.06f);
                g.FillRoundedRect(edgeBrush, shadowRect, s * 0.04f);

                // -- Clock overlay (bottom-right quadrant) ----------------------
                float cr = s * 0.26f;            // clock radius
                float cx = s * 0.72f;            // clock centre X
                float cy = s * 0.70f;            // clock centre Y

                // Clock face background
                using var clockBg = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
                g.FillEllipse(clockBg, cx - cr, cy - cr, cr * 2, cr * 2);

                // Clock border
                using var clockPen = new Pen(Color.FromArgb(200, 70, 130, 180), s * 0.04f);
                g.DrawEllipse(clockPen, cx - cr, cy - cr, cr * 2, cr * 2);

                // Hour hand (pointing ~10 o'clock)
                float hourAngle = (float)(Math.PI * 2 * (10.0 / 12.0) - Math.PI / 2);
                float hourLen   = cr * 0.55f;
                using var handPen = new Pen(Color.FromArgb(220, 50, 50, 50), s * 0.045f);
                handPen.StartCap = LineCap.Round;
                handPen.EndCap   = LineCap.Round;
                g.DrawLine(handPen, cx, cy,
                    cx + (float)Math.Cos(hourAngle) * hourLen,
                    cy + (float)Math.Sin(hourAngle) * hourLen);

                // Minute hand (pointing ~12 o'clock)
                float minAngle = (float)(-Math.PI / 2);
                float minLen   = cr * 0.75f;
                g.DrawLine(handPen, cx, cy,
                    cx + (float)Math.Cos(minAngle) * minLen,
                    cy + (float)Math.Sin(minAngle) * minLen);

                // Centre dot
                using var dotBrush = new SolidBrush(Color.FromArgb(220, 50, 50, 50));
                g.FillEllipse(dotBrush, cx - s * 0.035f, cy - s * 0.035f, s * 0.07f, s * 0.07f);
            }

            return IconFromBitmap(bmp);
        }

        /// <summary>
        /// Converts a <see cref="Bitmap"/> to a Windows <see cref="Icon"/>.
        /// </summary>
        private static Icon IconFromBitmap(Bitmap bmp)
        {
            using var ms = new MemoryStream();

            // Write a minimal ICO file manually
            int imgCount = 1;
            int width    = bmp.Width;
            int height   = bmp.Height;

            // PNG-encode the bitmap
            using var pngMs = new MemoryStream();
            bmp.Save(pngMs, ImageFormat.Png);
            byte[] pngData = pngMs.ToArray();

            // ICO header
            Write16(ms, 0);          // reserved
            Write16(ms, 1);          // type: ICO
            Write16(ms, imgCount);   // image count

            // Directory entry
            ms.WriteByte((byte)(width  > 255 ? 0 : width));
            ms.WriteByte((byte)(height > 255 ? 0 : height));
            ms.WriteByte(0);  // color count (0 = > 256)
            ms.WriteByte(0);  // reserved
            Write16(ms, 1);   // color planes
            Write16(ms, 32);  // bits per pixel
            Write32(ms, pngData.Length);
            Write32(ms, 6 + 16 * imgCount); // offset to image data

            ms.Write(pngData, 0, pngData.Length);
            ms.Seek(0, SeekOrigin.Begin);

            return new Icon(ms);
        }

        private static void Write16(Stream s, int value)
        {
            s.WriteByte((byte)(value & 0xFF));
            s.WriteByte((byte)((value >> 8) & 0xFF));
        }
        private static void Write32(Stream s, int value)
        {
            s.WriteByte((byte)(value & 0xFF));
            s.WriteByte((byte)((value >> 8) & 0xFF));
            s.WriteByte((byte)((value >> 16) & 0xFF));
            s.WriteByte((byte)((value >> 24) & 0xFF));
        }
    }

    // -- Extension method for rounded rectangles --------------------------------

    /// <summary>
    /// Graphics extension helpers used by <see cref="IconFactory"/>.
    /// </summary>
    internal static class GraphicsExtensions
    {
        /// <summary>Fills a rectangle with rounded corners.</summary>
        public static void FillRoundedRect(this Graphics g, Brush brush,
                                           RectangleF rect, float radius)
        {
            using var path = RoundedRectPath(rect, radius);
            g.FillPath(brush, path);
        }

        private static GraphicsPath RoundedRectPath(RectangleF rect, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
