using System.Drawing.Drawing2D;

namespace VsCodeTabs;

internal sealed class TabStrip : Form
{
    const TextFormatFlags LabelFormat = TextFormatFlags.Left | TextFormatFlags.VerticalCenter
        | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    const TextFormatFlags MeasureFormat = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    const int AppIconSlotDip = 35;
    const int AppIconDip = 16;

    readonly TabGroup group;
    readonly ToolTip toolTip = new() { ShowAlways = true };
    readonly List<(IntPtr Hwnd, Rectangle Tab, Rectangle Close, TabLabel Label)> layout = [];
    Palette palette = Palette.From(Color.FromArgb(0x18, 0x18, 0x18), Color.Empty, Color.Empty);
    Rectangle plus;
    float scale = 1f;
    Font nameFont;
    Font detailFont;
    int hoverIndex = -1;
    bool hoverClose;
    bool hoverPlus;
    int dragIndex = -1;
    Point dragOrigin;
    bool dragging;
    int dropIndex = -1;
    readonly System.Windows.Forms.Timer pollTimer = new() { Interval = 30 };
    bool passThrough;
    IntPtr owner;
    Rectangle placed;
    int iconArea;
    Icon? appIcon;
    (string Path, int Size) appIconKey;
    string toolTipText = "";

    public TabStrip(TabGroup group)
    {
        this.group = group;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        BackColor = palette.Background;
        Text = "VS Code Tabs";
        (nameFont, detailFont) = CreateFonts(scale);
        pollTimer.Tick += (_, _) =>
        {
            Recover();
            StayOnTop();
            UpdatePassThrough();
            if (group.PendingTab || group.LoadingTab != IntPtr.Zero)
                Invalidate();
        };
        pollTimer.Start();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_LAYERED;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        passThrough = false;
        ResetLayering();
    }

    protected override bool ShowWithoutActivation => true;

    public Palette Colors => palette;

    public float DpiScale => scale;

    public Rectangle ScreenBounds
    {
        get
        {
            if (!Native.IsWindowVisible(Handle))
                return Rectangle.Empty;
            Native.GetWindowRect(Handle, out var rect);
            return rect.ToRectangle();
        }
    }

    public Rectangle TabAreaScreenBounds
    {
        get
        {
            var bounds = ScreenBounds;
            return bounds.IsEmpty ? bounds : Rectangle.FromLTRB(bounds.Left + iconArea, bounds.Top, bounds.Right, bounds.Bottom);
        }
    }

    public void Attach(IntPtr parent)
    {
        var handle = Handle;
        if (parent == owner && Native.GetParent(handle) == parent)
            return;
        owner = parent;
        var wasTopLevel = Native.GetParent(handle) == IntPtr.Zero;
        var style = Native.GetWindowLongPtr(handle, Native.GWL_STYLE).ToInt64();
        style = (style & ~Native.WS_POPUP) | Native.WS_CHILD | Native.WS_CLIPSIBLINGS;
        Native.SetWindowLongPtr(handle, Native.GWL_STYLE, new IntPtr(style));
        Native.SetParent(handle, parent);
        if (wasTopLevel)
            ResetLayering();
    }

    void ResetLayering()
    {
        var exStyle = Native.GetWindowLongPtr(Handle, Native.GWL_EXSTYLE).ToInt64();
        Native.SetWindowLongPtr(Handle, Native.GWL_EXSTYLE, new IntPtr(exStyle & ~(long)Native.WS_EX_LAYERED));
        Native.SetWindowLongPtr(Handle, Native.GWL_EXSTYLE, new IntPtr(exStyle | Native.WS_EX_LAYERED));
        Native.SetLayeredWindowAttributes(Handle, 0, 255, Native.LWA_ALPHA);
    }

    public IntPtr AttachedTo => owner;

    public void Mirror(TabStrip source, IntPtr parent)
    {
        SetScale(source.scale);
        iconArea = source.iconArea;
        placed = source.placed;
        if (palette != source.palette)
        {
            palette = source.palette;
            BackColor = palette.Background;
        }
        Attach(parent);
        Native.GetWindowRect(source.Handle, out var rect);
        var bounds = rect.ToRectangle();
        var origin = bounds.Location;
        Native.ScreenToClient(source.owner, ref origin);
        Native.SetWindowPos(Handle, Native.HWND_TOP, origin.X, origin.Y, bounds.Width, bounds.Height,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        Invalidate();
        Update();
    }

    void SetScale(float newScale)
    {
        if (Math.Abs(newScale - scale) <= 0.001f)
            return;
        scale = newScale;
        nameFont.Dispose();
        detailFont.Dispose();
        (nameFont, detailFont) = CreateFonts(scale);
    }

    public void Place(Rectangle bounds, float newScale, int newIconArea)
    {
        iconArea = newIconArea;
        SetScale(newScale);
        placed = bounds;
        if (owner != IntPtr.Zero)
            Attach(owner);
        var origin = bounds.Location;
        Native.ScreenToClient(owner, ref origin);
        Native.SetWindowPos(Handle, Native.HWND_TOP, origin.X, origin.Y, bounds.Width, bounds.Height,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        Invalidate();
        Update();
    }

    void Recover()
    {
        if (owner == IntPtr.Zero || !Native.IsWindow(owner))
            return;
        if (IsHandleCreated && Native.GetParent(Handle) == owner)
            return;
        Place(placed, scale, iconArea);
    }

    public void HideStrip()
    {
        hoverIndex = -1;
        hoverClose = false;
        hoverPlus = false;
        dropIndex = -1;
        if (Native.IsWindowVisible(Handle))
            Native.ShowWindow(Handle, Native.SW_HIDE);
    }

    public void ApplyTheme(TitleColors colors)
    {
        var next = Palette.From(colors.Background, colors.Foreground, colors.Edge);
        if (next == palette)
            return;
        palette = next;
        BackColor = colors.Background;
        Invalidate();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_MOUSEACTIVATE)
        {
            m.Result = Native.MA_NOACTIVATE;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Arrange();
        var graphics = e.Graphics;
        graphics.Clear(palette.Background);
        DrawAppIcon(graphics);

        bool IsActive(int index) => group.PendingTab ? layout[index].Hwnd == IntPtr.Zero : layout[index].Hwnd == group.Active;
        bool IsHovered(int index) => index == hoverIndex && layout[index].Hwnd != IntPtr.Zero;
        bool IsHighlighted(int index) => IsActive(index) || IsHovered(index);

        using (var separator = new SolidBrush(palette.Separator))
        {
            for (var i = 0; i + 1 < layout.Count; i++)
            {
                if (IsHighlighted(i) || IsHighlighted(i + 1))
                    continue;
                var x = (layout[i].Tab.Right + layout[i + 1].Tab.Left) / 2;
                var height = S(16);
                graphics.FillRectangle(separator, x, (ClientSize.Height - height) / 2, Math.Max(1, S(1)), height);
            }
        }

        for (var i = 0; i < layout.Count; i++)
        {
            var (hwnd, tab, close, label) = layout[i];
            var isPlaceholder = hwnd == IntPtr.Zero;
            var isActive = IsActive(i);
            var isHovered = IsHovered(i);
            var opacity = isPlaceholder || hwnd == group.LoadingTab
                ? PulseOpacity(Environment.TickCount64 - group.LoadingSince)
                : 1.0;
            Color Fade(Color color) => opacity >= 1.0 ? color : Blend(color, palette.Background, opacity);

            if (isActive || isHovered)
                FillRounded(graphics, Fade(isActive ? palette.ActiveBackground : palette.HoverBackground), tab,
                    isActive && !palette.ActiveBorder.IsEmpty ? Fade(palette.ActiveBorder) : Color.Empty);

            var textRight = isActive || isHovered ? close.Left - S(4) : tab.Right - S(10);
            var nameBounds = Rectangle.FromLTRB(tab.Left + S(10), tab.Top, textRight, tab.Bottom);
            TextRenderer.DrawText(graphics, label.Name, nameFont, nameBounds,
                Fade(isActive ? palette.ActiveText : palette.InactiveText), LabelFormat);

            if (label.Detail.Length > 0)
            {
                var nameWidth = TextRenderer.MeasureText(graphics, label.Name, nameFont, Size.Empty, MeasureFormat).Width;
                var detailBounds = Rectangle.FromLTRB(nameBounds.Left + nameWidth + S(6), tab.Top, textRight, tab.Bottom);
                if (detailBounds.Width > S(12))
                    TextRenderer.DrawText(graphics, label.Detail, detailFont, detailBounds, Fade(palette.DetailText), LabelFormat);
            }

            if ((isActive || isHovered) && !isPlaceholder)
            {
                if (isHovered && hoverClose)
                    FillRounded(graphics, palette.ButtonHover, close);
                DrawGlyph(graphics, close, Fade(isActive ? palette.ActiveText : palette.InactiveText), cross: true);
            }
        }

        if (hoverPlus)
            FillRounded(graphics, palette.ButtonHover, plus);
        DrawGlyph(graphics, plus, palette.InactiveText, cross: false);

        if (dropIndex >= 0 && layout.Count > 0)
        {
            var edge = dropIndex < layout.Count ? layout[dropIndex].Tab.Left - S(1) : layout[^1].Tab.Right + S(1);
            var tab = layout[0].Tab;
            using var marker = new SolidBrush(palette.ActiveText);
            graphics.FillRectangle(marker, edge - S(1), tab.Top, Math.Max(2, S(2)), tab.Height);
        }
    }

    public void SetDropIndex(int index)
    {
        if (index == dropIndex)
            return;
        dropIndex = index;
        Invalidate();
    }

    public Rectangle TabScreenBounds(IntPtr hwnd)
    {
        Arrange();
        var index = layout.FindIndex(entry => entry.Hwnd == hwnd);
        if (index < 0)
            return Rectangle.Empty;
        var tab = layout[index].Tab;
        var origin = ScreenBounds.Location;
        return new Rectangle(origin.X + tab.X, origin.Y + tab.Y, tab.Width, tab.Height);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Arrange();
        if (e.Button != MouseButtons.Left)
            return;
        var index = IndexAt(e.Location);
        if (index < 0 || layout[index].Hwnd == IntPtr.Zero || layout[index].Close.Contains(e.Location))
            return;

        dragIndex = index;
        dragOrigin = e.Location;
        dragging = false;
        group.Switch(layout[index].Hwnd);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Arrange();
        if (dragIndex >= 0 && e.Button == MouseButtons.Left)
        {
            if (!dragging && dragIndex < layout.Count
                && (Math.Abs(e.X - dragOrigin.X) > S(4) || Math.Abs(e.Y - dragOrigin.Y) > S(4)))
            {
                dragging = true;
                var tab = layout[dragIndex].Tab;
                group.BeginDrag(layout[dragIndex].Hwnd, new Point(dragOrigin.X - tab.Left, dragOrigin.Y - tab.Top));
            }
            return;
        }

        var hovered = IndexAt(e.Location);
        var overClose = hovered >= 0 && layout[hovered].Close.Contains(e.Location);
        var overPlus = plus.Contains(e.Location);
        if (hovered != hoverIndex || overClose != hoverClose || overPlus != hoverPlus)
        {
            hoverIndex = hovered;
            hoverClose = overClose;
            hoverPlus = overPlus;
            Invalidate();
        }

        var text = hovered >= 0 ? Win.Title(layout[hovered].Hwnd) : "";
        if (text != toolTipText)
        {
            toolTipText = text;
            toolTip.SetToolTip(this, text);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        Arrange();
        var index = IndexAt(e.Location);
        if (index >= 0 && layout[index].Hwnd == IntPtr.Zero)
            index = -1;
        if (e.Button == MouseButtons.Left && !dragging)
        {
            if (index >= 0 && layout[index].Close.Contains(e.Location))
                group.Close(layout[index].Hwnd);
            else if (plus.Contains(e.Location))
                group.NewTab();
        }
        else if (e.Button == MouseButtons.Middle && index >= 0)
        {
            group.Close(layout[index].Hwnd);
        }
        dragIndex = -1;
        dragging = false;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hoverIndex = -1;
        hoverClose = false;
        hoverPlus = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            nameFont.Dispose();
            detailFont.Dispose();
            toolTip.Dispose();
            pollTimer.Dispose();
            appIcon?.Dispose();
        }
        base.Dispose(disposing);
    }

    void StayOnTop()
    {
        if (owner == IntPtr.Zero || !Native.IsWindowVisible(Handle))
            return;
        var above = Native.GetWindow(Handle, Native.GW_HWNDPREV);
        while (above != IntPtr.Zero && !Native.IsWindowVisible(above))
            above = Native.GetWindow(above, Native.GW_HWNDPREV);
        if (above != IntPtr.Zero)
            Native.SetWindowPos(Handle, Native.HWND_TOP, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    void UpdatePassThrough()
    {
        if (!Native.IsWindowVisible(Handle) || MouseButtons != MouseButtons.None)
            return;
        Native.GetCursorPos(out var cursor);
        var bounds = ScreenBounds;
        if (!bounds.Contains(cursor))
            return;
        Arrange();
        var point = new Point(cursor.X - bounds.X, cursor.Y - bounds.Y);
        SetPassThrough(IndexAt(point) < 0 && !plus.Contains(point));
    }

    void SetPassThrough(bool value)
    {
        if (value == passThrough)
            return;
        passThrough = value;
        var style = Native.GetWindowLongPtr(Handle, Native.GWL_EXSTYLE).ToInt64();
        style = value ? style | Native.WS_EX_TRANSPARENT : style & ~(long)Native.WS_EX_TRANSPARENT;
        Native.SetWindowLongPtr(Handle, Native.GWL_EXSTYLE, new IntPtr(style));
    }

    void Arrange()
    {
        layout.Clear();
        var tabs = group.PendingTab ? group.Tabs.Append(IntPtr.Zero).ToList() : group.Tabs;
        var height = Math.Min(S(24), ClientSize.Height - S(4));
        var top = (ClientSize.Height - height) / 2;
        var gap = S(2);
        var closeSize = S(18);
        var plusSize = S(22);
        var labels = tabs.Select(hwnd => TitleParser.Label(hwnd == IntPtr.Zero ? "Visual Studio Code" : Win.Title(hwnd))).ToList();
        var widths = labels.Select(TabWidth).ToList();

        var iconMargin = (S(AppIconSlotDip) - S(AppIconDip)) / 2;
        var x = iconMargin + S(AppIconDip) + iconMargin;
        var available = Math.Max(0, ClientSize.Width - x - plusSize - S(8) - gap * tabs.Count);
        if (widths.Count > 0 && widths.Sum() > available)
            widths = Enumerable.Repeat(available / widths.Count, widths.Count).ToList();

        for (var i = 0; i < tabs.Count; i++)
        {
            var tab = new Rectangle(x, top, widths[i], height);
            var close = new Rectangle(tab.Right - closeSize - S(3), top + (height - closeSize) / 2, closeSize, closeSize);
            layout.Add((tabs[i], tab, close, labels[i]));
            x += widths[i] + gap;
        }

        plus = new Rectangle(x + S(4), (ClientSize.Height - plusSize) / 2, plusSize, plusSize);
    }

    void DrawAppIcon(Graphics graphics)
    {
        var size = S(AppIconDip);
        if (iconArea < size || owner == IntPtr.Zero || Win.ProcessPath(owner) is not { } path)
            return;
        if (appIconKey != (path, size))
        {
            appIcon?.Dispose();
            appIcon = Icon.ExtractIcon(path, 0, size);
            appIconKey = (path, size);
        }
        if (appIcon is not null)
            graphics.DrawIcon(appIcon, new Rectangle((S(AppIconSlotDip) - size) / 2, (ClientSize.Height - size) / 2, size, size));
    }

    int TabWidth(TabLabel label) => Math.Clamp(PreferredWidth(label) + S(18) + S(18), S(110), S(320));

    int PreferredWidth(TabLabel label)
    {
        var width = TextRenderer.MeasureText(label.Name, nameFont, Size.Empty, MeasureFormat).Width;
        if (label.Detail.Length > 0)
            width += S(6) + TextRenderer.MeasureText(label.Detail, detailFont, Size.Empty, MeasureFormat).Width;
        return width;
    }

    int IndexAt(Point point) => layout.FindIndex(entry => entry.Tab.Contains(point));

    public int IndexAtScreen(int screenX, bool insert)
    {
        Arrange();
        var x = screenX - ScreenBounds.Left;
        var index = layout.FindIndex(entry => x < (insert ? entry.Tab.Left + entry.Tab.Width / 2 : entry.Tab.Right));
        if (index >= 0)
            return index;
        return insert ? layout.Count : Math.Max(0, layout.Count - 1);
    }

    void FillRounded(Graphics graphics, Color color, Rectangle bounds, Color border = default)
    {
        var outline = border.IsEmpty ? bounds : new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        var diameter = S(5) * 2;
        using var path = new GraphicsPath();
        path.AddArc(outline.Left, outline.Top, diameter, diameter, 180, 90);
        path.AddArc(outline.Right - diameter, outline.Top, diameter, diameter, 270, 90);
        path.AddArc(outline.Right - diameter, outline.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(outline.Left, outline.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        using var brush = new SolidBrush(color);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.FillPath(brush, path);
        if (!border.IsEmpty)
        {
            using var pen = new Pen(border, Math.Max(1f, (float)Math.Floor(scale)));
            graphics.DrawPath(pen, path);
        }
        graphics.SmoothingMode = SmoothingMode.Default;
    }

    static double PulseOpacity(long elapsed)
    {
        var phase = elapsed % 2000 / 1000.0;
        var eased = EaseInOut(phase < 1 ? phase : phase - 1);
        return phase < 1 ? 0.5 + 0.5 * eased : 1 - 0.5 * eased;
    }

    static double EaseInOut(double x)
    {
        double low = 0, high = 1;
        for (var i = 0; i < 20; i++)
        {
            var t = (low + high) / 2;
            if (Bezier(t, 0.4, 0.6) < x)
                low = t;
            else
                high = t;
        }
        return Bezier((low + high) / 2, 0, 1);
    }

    static double Bezier(double t, double first, double second) =>
        3 * (1 - t) * (1 - t) * t * first + 3 * (1 - t) * t * t * second + t * t * t;

    static Color Blend(Color color, Color toward, double amount) => Color.FromArgb(
        (int)Math.Round(toward.R + (color.R - toward.R) * amount),
        (int)Math.Round(toward.G + (color.G - toward.G) * amount),
        (int)Math.Round(toward.B + (color.B - toward.B) * amount));

    void DrawGlyph(Graphics graphics, Rectangle bounds, Color color, bool cross)
    {
        using var pen = new Pen(color, Math.Max(1f, scale));
        var inset = bounds.Width * (cross ? 0.26f : 0.2f);
        var left = bounds.Left + inset;
        var top = bounds.Top + inset;
        var right = bounds.Right - inset;
        var bottom = bounds.Bottom - inset;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (cross)
        {
            graphics.DrawLine(pen, left, top, right, bottom);
            graphics.DrawLine(pen, left, bottom, right, top);
        }
        else
        {
            var centerX = (left + right) / 2;
            var centerY = (top + bottom) / 2;
            graphics.DrawLine(pen, left, centerY, right, centerY);
            graphics.DrawLine(pen, centerX, top, centerX, bottom);
        }
        graphics.SmoothingMode = SmoothingMode.Default;
    }

    int S(int value) => (int)Math.Round(value * scale);

    public static (Font Name, Font Detail) CreateFonts(float scale) =>
        (new Font("Segoe UI", 13f * scale, GraphicsUnit.Pixel), new Font("Segoe UI", 12f * scale, GraphicsUnit.Pixel));
}
