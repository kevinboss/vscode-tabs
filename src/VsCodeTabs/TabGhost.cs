using System.Drawing.Drawing2D;

namespace VsCodeTabs;

internal sealed class TabGhost : Form
{
    const TextFormatFlags LabelFormat = TextFormatFlags.Left | TextFormatFlags.VerticalCenter
        | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    const TextFormatFlags MeasureFormat = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    const byte GhostAlpha = 230;

    readonly TabLabel label;
    readonly Palette palette;
    readonly float scale;
    readonly Font nameFont;
    readonly Font detailFont;

    public TabGhost(TabLabel label, Palette palette, float scale, Size size)
    {
        this.label = label;
        this.palette = palette;
        this.scale = scale;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        BackColor = palette.Background;
        Size = size;
        (nameFont, detailFont) = TabStrip.CreateFonts(scale);
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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetLayeredWindowAttributes(Handle, 0, GhostAlpha, Native.LWA_ALPHA);
    }

    public void MoveTo(Point location) =>
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, location.X, location.Y, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

    public void HideGhost()
    {
        if (IsHandleCreated && Native.IsWindowVisible(Handle))
            Native.ShowWindow(Handle, Native.SW_HIDE);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(palette.Background);
        var bounds = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        var diameter = S(5) * 2;
        using (var path = new GraphicsPath())
        {
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            using var fill = new SolidBrush(palette.ActiveBackground);
            using var border = new Pen(palette.ActiveBorder.IsEmpty ? palette.ButtonHover : palette.ActiveBorder);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
            graphics.SmoothingMode = SmoothingMode.Default;
        }

        var nameBounds = Rectangle.FromLTRB(S(10), 0, ClientSize.Width - S(10), ClientSize.Height);
        TextRenderer.DrawText(graphics, label.Name, nameFont, nameBounds, palette.ActiveText, LabelFormat);
        if (label.Detail.Length > 0)
        {
            var nameWidth = TextRenderer.MeasureText(graphics, label.Name, nameFont, Size.Empty, MeasureFormat).Width;
            var detailBounds = Rectangle.FromLTRB(nameBounds.Left + nameWidth + S(6), 0, nameBounds.Right, ClientSize.Height);
            if (detailBounds.Width > S(12))
                TextRenderer.DrawText(graphics, label.Detail, detailFont, detailBounds, palette.DetailText, LabelFormat);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            nameFont.Dispose();
            detailFont.Dispose();
        }
        base.Dispose(disposing);
    }

    int S(int value) => (int)Math.Round(value * scale);
}
