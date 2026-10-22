using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace ShieldBlock.Setup
{
    public static class IconHelper
    {
        public static Icon CreateShieldIcon(int size = 256)
        {
            using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                int pad = size / 20;
                int rad = size / 6;
                using var path = new GraphicsPath();
                int w = size - (pad * 2);
                int h = size - (pad * 2);
                path.AddArc(pad, pad, rad * 2, rad * 2, 180, 90);
                path.AddArc(pad + w - rad * 2, pad, rad * 2, rad * 2, 270, 90);
                path.AddArc(pad + w - rad * 2, pad + h - rad * 2, rad * 2, rad * 2, 0, 90);
                path.AddArc(pad, pad + h - rad * 2, rad * 2, rad * 2, 90, 90);
                path.CloseFigure();

                using var bgBrush = new SolidBrush(Color.FromArgb(11, 15, 25));
                g.FillPath(bgBrush, path);

                using var borderPen = new Pen(Color.FromArgb(0, 229, 255), size / 32f);
                g.DrawPath(borderPen, path);

                var shieldPts = new PointF[]
                {
                    new PointF(size * 0.25f, size * 0.22f),
                    new PointF(size * 0.75f, size * 0.22f),
                    new PointF(size * 0.82f, size * 0.52f),
                    new PointF(size * 0.50f, size * 0.82f),
                    new PointF(size * 0.18f, size * 0.52f)
                };

                using var gradBrush = new LinearGradientBrush(
                    new PointF(size * 0.5f, size * 0.2f),
                    new PointF(size * 0.5f, size * 0.8f),
                    Color.FromArgb(0, 229, 255),
                    Color.FromArgb(16, 185, 129));
                g.FillPolygon(gradBrush, shieldPts);

                using var checkPen = new Pen(Color.White, size / 16f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round
                };
                g.DrawLines(checkPen, new PointF[]
                {
                    new PointF(size * 0.35f, size * 0.50f),
                    new PointF(size * 0.47f, size * 0.62f),
                    new PointF(size * 0.67f, size * 0.38f)
                });
            }

            IntPtr hIcon = bmp.GetHicon();
            Icon icon = Icon.FromHandle(hIcon);
            return icon;
        }
    }
}
