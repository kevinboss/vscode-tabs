using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace VsCodeTabs;

internal readonly record struct TitleBar(int Top, int Height, int LeftInset, int RightInset, Color Background);

internal static class TitleBarProbe
{
    const int Tolerance = 30;
    const int ButtonGapDip = 60;

    public static TitleBar? Measure(IntPtr hwnd, Rectangle visible, float scale, Rectangle exclude)
    {
        int S(int value) => (int)Math.Round(value * scale);

        Native.GetWindowRect(hwnd, out var outerRect);
        var outer = outerRect.ToRectangle();
        var originX = visible.Left - outer.Left;
        var originY = visible.Top - outer.Top;
        var width = visible.Width;
        var height = Math.Min(S(80), visible.Height);
        if (outer.Width <= 0 || outer.Height <= 0 || width <= S(200) || originX < 0 || originY < 0
            || originX + width > outer.Width || originY + height > outer.Height)
            return null;

        var pixels = Capture(hwnd, outer.Size, new Rectangle(originX, originY, width, height));
        if (pixels is null)
            return null;
        int At(int x, int y) => pixels[y * width + x];

        var excludedLeft = exclude.IsEmpty ? -1 : exclude.Left - visible.Left;
        var excludedRight = exclude.IsEmpty ? -1 : exclude.Right - visible.Left;
        bool IsExcluded(int x) => x >= excludedLeft && x < excludedRight;
        var columns = Enumerable.Range(0, width).Where(x => !IsExcluded(x)).ToList();

        var probeRow = S(6);
        var background = columns.Select(x => At(x, probeRow))
            .GroupBy(pixel => pixel).MaxBy(group => group.Count())!.Key;
        bool IsBackground(int pixel) => Distance(pixel, background) <= Tolerance;

        var candidates = columns.Where(x => IsBackground(At(x, probeRow))).ToList();
        if (candidates.Count == 0)
            return null;

        var top = Mode(candidates.Select(x =>
        {
            var y = probeRow;
            while (y > 0 && IsBackground(At(x, y - 1)))
                y--;
            return y;
        }).ToList());

        var heights = candidates.Select(x =>
        {
            var y = probeRow;
            while (y + 1 < height && IsBackground(At(x, y + 1)))
                y++;
            return y + 1 - top;
        }).Where(span => span >= S(16) && span <= S(60)).ToList();
        if (heights.Count == 0)
            return null;
        var bottom = top + Mode(heights);

        var occupied = new bool[width];
        foreach (var x in columns)
        {
            for (var y = top + S(2); y < bottom - S(2) && !occupied[x]; y++)
                occupied[x] = !IsBackground(At(x, y));
        }

        var leftEnd = 0;
        var gap = 0;
        for (var x = 0; x < Math.Min(width, S(160)) && gap <= S(24); x++)
        {
            if (occupied[x])
            {
                leftEnd = x + 1;
                gap = 0;
            }
            else if (leftEnd > 0)
            {
                gap++;
            }
        }

        var rightStart = width;
        gap = 0;
        for (var x = width - 1; x >= width / 2 && gap <= S(ButtonGapDip); x--)
        {
            if (occupied[x])
            {
                rightStart = x;
                gap = 0;
            }
            else
            {
                gap++;
            }
        }

        if (leftEnd == 0 || rightStart == width)
            return null;

        return new TitleBar(top, bottom - top, leftEnd + S(8), width - rightStart + S(ButtonGapDip) + S(8), Color.FromArgb(background));
    }

    static int[]? Capture(IntPtr hwnd, Size size, Rectangle region)
    {
        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var dc = graphics.GetHdc();
            try
            {
                if (!Native.PrintWindow(hwnd, dc, Native.PW_RENDERFULLCONTENT))
                    return null;
            }
            finally
            {
                graphics.ReleaseHdc(dc);
            }
        }

        var data = bitmap.LockBits(region, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            var pixels = new int[region.Width * region.Height];
            for (var y = 0; y < region.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * region.Width, region.Width);
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] |= unchecked((int)0xFF000000);
            return pixels;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    static int Distance(int a, int b) =>
        Math.Abs(((a >> 16) & 0xFF) - ((b >> 16) & 0xFF))
        + Math.Abs(((a >> 8) & 0xFF) - ((b >> 8) & 0xFF))
        + Math.Abs((a & 0xFF) - (b & 0xFF));

    static int Mode(List<int> values) => values.GroupBy(value => value).MaxBy(group => group.Count())!.Key;
}
