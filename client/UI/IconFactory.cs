using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using VlessMonitor.Core;

namespace VlessMonitor.UI;

/// <summary>Generates colored traffic-light tray icons at runtime.</summary>
public static class IconFactory
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static readonly Color Green  = Color.FromArgb(0xA6, 0xE3, 0xA1);
    public static readonly Color Yellow = Color.FromArgb(0xF9, 0xE2, 0xAF);
    public static readonly Color Red    = Color.FromArgb(0xF3, 0x8B, 0xA8);
    public static readonly Color Grey   = Color.FromArgb(0x6C, 0x70, 0x86);

    private static (Color fill, Color glow) Palette(OverallStatus s) => s switch
    {
        OverallStatus.Green  => (Green,  Color.FromArgb(0x4A, 0xB4, 0x44)),
        OverallStatus.Yellow => (Yellow, Color.FromArgb(0xD2, 0xA0, 0x14)),
        OverallStatus.Red    => (Red,    Color.FromArgb(0xC8, 0x32, 0x50)),
        _                    => (Grey,   Color.FromArgb(0x3C, 0x40, 0x60)),
    };

    public static Icon Create(OverallStatus status, int size = 32)
    {
        var (fill, glow) = Palette(status);
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            int pad = size / 8;
            var rect = new Rectangle(pad, pad, size - 2 * pad, size - 2 * pad);

            // Soft outer glow
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(0, 0, size, size);
                using var pgb = new PathGradientBrush(path)
                {
                    CenterColor = Color.FromArgb(120, glow),
                    SurroundColors = new[] { Color.FromArgb(0, glow) },
                };
                g.FillEllipse(pgb, 0, 0, size, size);
            }

            // Main circle
            using (var brush = new SolidBrush(fill))
                g.FillEllipse(brush, rect);
            using (var pen = new Pen(glow, 1.5f))
                g.DrawEllipse(pen, rect);

            // Highlight shine
            var shineRect = new Rectangle(
                rect.X + rect.Width / 5, rect.Y + rect.Height / 6,
                rect.Width / 2, rect.Height / 3);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(shineRect);
                using var pgb = new PathGradientBrush(path)
                {
                    CenterColor = Color.FromArgb(150, Color.White),
                    SurroundColors = new[] { Color.FromArgb(0, Color.White) },
                };
                g.FillEllipse(pgb, shineRect);
            }
        }

        var hIcon = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hIcon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }
}
