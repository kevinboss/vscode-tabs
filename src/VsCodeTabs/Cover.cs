using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace VsCodeTabs;

internal sealed class Cover : Form
{
    Cover()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_LAYERED
                | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST;
            return parameters;
        }
    }

    protected override bool ShowWithoutActivation => true;

    public static Cover? Over(Rectangle bounds, Rectangle hole)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        using var screen = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(screen))
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        using var canvas = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(canvas))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(screen, 0, 0);
            if (!hole.IsEmpty)
            {
                using var clear = new SolidBrush(Color.Transparent);
                graphics.FillRectangle(clear, hole with { X = hole.X - bounds.X, Y = hole.Y - bounds.Y });
            }
        }

        var cover = new Cover();
        cover.Present(canvas, bounds);
        return cover;
    }

    void Present(Bitmap canvas, Rectangle bounds)
    {
        var screenDc = Native.GetDC(IntPtr.Zero);
        var memoryDc = Native.CreateCompatibleDC(screenDc);
        var bitmap = canvas.GetHbitmap(Color.FromArgb(0));
        var previous = Native.SelectObject(memoryDc, bitmap);
        try
        {
            var destination = bounds.Location;
            var size = bounds.Size;
            var source = Point.Empty;
            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER,
                SourceConstantAlpha = 255,
                AlphaFormat = Native.AC_SRC_ALPHA,
            };
            Native.UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend,
                Native.ULW_ALPHA);
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }
        finally
        {
            Native.SelectObject(memoryDc, previous);
            Native.DeleteObject(bitmap);
            Native.DeleteDC(memoryDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
