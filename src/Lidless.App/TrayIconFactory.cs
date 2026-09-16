using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Lidless.App;

internal static class TrayIconFactory
{
    public static Icon Create(bool active)
    {
        var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        var color = active ? Color.FromArgb(48, 209, 88) : Color.FromArgb(245, 245, 247);
        using var pen = new Pen(color, 1.4f);
        using var brush = new SolidBrush(color);
        g.DrawRectangle(pen, 3, 2, 10, 7);
        g.FillRectangle(brush, 1, 11, 14, 2);
        g.FillRectangle(brush, 7, 9, 2, 2);

        var handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
            bmp.Dispose();
        }
    }
}
