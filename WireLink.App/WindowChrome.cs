using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WireLink.App;

// The OS keeps a real window title for taskbar/Alt+Tab; we draw no caption text.
public class ChromeForm : Form
{
    private const int ResizeBorder = 6;
    private CaptionBar? caption;

    private protected void InstallChrome(WindowPalette palette, Action editTheme)
    {
        FormBorderStyle = FormBorderStyle.None;
        BackColor = palette.Header;
        Padding = new Padding(ResizeBorder);
        caption = new CaptionBar(this, palette, editTheme) { Dock = DockStyle.Top, Height = 32 };
        Controls.Add(caption);
        caption.SendToBack(); // Dock the top strip before the fill content.
    }

    private protected void ApplyChromePalette(WindowPalette palette)
    {
        BackColor = palette.Header;
        caption?.ApplyPalette(palette);
    }

    private protected void SetCaptionOpacity(int opacity) => caption?.SetBackgroundOpacity(opacity);

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // Keep resize, system menu and minimize/maximize semantics without WS_CAPTION.
            parameters.Style = (parameters.Style & ~0x00C00000) | 0x00040000 | 0x00080000 | 0x00020000 | 0x00010000;
            return parameters;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Padding = WindowState == FormWindowState.Maximized ? Padding.Empty : new Padding((int)Math.Round(ResizeBorder * DeviceDpi / 96f));
        caption?.RefreshWindowState();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0083) // WM_NCCALCSIZE: the entire frame is our client area.
        {
            message.Result = IntPtr.Zero;
            return;
        }
        if (message.Msg == 0x0086 && WindowState != FormWindowState.Minimized) // WM_NCACTIVATE
        {
            // Keep activation semantics but forbid DefWindowProc from repainting the
            // WS_THICKFRAME sizing border when focus moves to another window.
            message.LParam = (IntPtr)(-1);
            base.WndProc(ref message);
            return;
        }
        if (message.Msg == 0x0085 && WindowState != FormWindowState.Minimized) // WM_NCPAINT
        {
            message.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref message);
        if (message.Msg == 0x0024) // WM_GETMINMAXINFO: maximize into the monitor's work area.
        {
            var limits = Marshal.PtrToStructure<MinMaxInfo>(message.LParam);
            var screen = Screen.FromHandle(Handle);
            limits.MaxPosition = new(screen.WorkingArea.Left - screen.Bounds.Left, screen.WorkingArea.Top - screen.Bounds.Top);
            limits.MaxSize = new(screen.WorkingArea.Width, screen.WorkingArea.Height);
            limits.MinTrackSize = new(MinimumSize.Width, MinimumSize.Height);
            Marshal.StructureToPtr(limits, message.LParam, false);
        }
        else if (message.Msg == 0x0084) // WM_NCHITTEST
        {
            var packed = message.LParam.ToInt64();
            var point = PointToClient(new Point(unchecked((short)(packed & 0xFFFF)), unchecked((short)((packed >> 16) & 0xFFFF))));
            var edge = Padding.Left;
            if (WindowState != FormWindowState.Maximized)
            {
                var left = point.X < edge; var right = point.X >= ClientSize.Width - edge;
                var top = point.Y < edge; var bottom = point.Y >= ClientSize.Height - edge;
                var hit = top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17
                    : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 0;
                if (hit != 0) { message.Result = (IntPtr)hit; return; }
            }
            if (caption?.IsDragPoint(point) == true) message.Result = (IntPtr)2; // HTCAPTION: drag, snap, double-click.
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint(int x, int y) { public int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo
    {
        public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }
}

internal sealed class CaptionBar : Control
{
    private readonly Form owner;
    private readonly CaptionButton[] buttons = [];
    private readonly CaptionButton theme, maximize;
    private readonly ToolTip tips = new();
    private WindowPalette palette;
    private int backgroundOpacity = 100;

    public CaptionBar(Form owner, WindowPalette palette, Action editTheme)
    {
        this.owner = owner; this.palette = palette;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AccessibleName = "窗口控制栏";
        theme = new("自定义主题", CaptionGlyph.Theme, palette, editTheme) { TabStop = true };
        var minimize = new CaptionButton("最小化", CaptionGlyph.Minimize, palette, () => owner.WindowState = FormWindowState.Minimized);
        maximize = new("最大化", CaptionGlyph.Maximize, palette, () => owner.WindowState = owner.WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal : FormWindowState.Maximized);
        var close = new CaptionButton("关闭", CaptionGlyph.Close, palette, owner.Close);
        buttons = [theme, minimize, maximize, close];
        tips.SetToolTip(theme, theme.AccessibleName);
        Controls.AddRange(buttons);
    }

    public void ApplyPalette(WindowPalette value)
    {
        palette = value;
        foreach (var button in buttons) button.ApplyPalette(value);
        Invalidate();
    }

    public void SetBackgroundOpacity(int opacity)
    {
        if (backgroundOpacity == opacity) return;
        backgroundOpacity = opacity;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, opacity == 100);
        foreach (var button in buttons) button.SetBackgroundOpacity(opacity);
        Invalidate();
    }

    public void RefreshWindowState()
    {
        maximize.Glyph = owner.WindowState == FormWindowState.Maximized ? CaptionGlyph.Restore : CaptionGlyph.Maximize;
        maximize.AccessibleName = owner.WindowState == FormWindowState.Maximized ? "还原窗口" : "最大化";
        maximize.Invalidate();
    }

    public bool IsDragPoint(Point parentPoint) => Bounds.Contains(parentPoint) && parentPoint.X < Left + theme.Left;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PositionButtons();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        PositionButtons();
    }

    private void PositionButtons()
    {
        var width = LogicalToDeviceUnits(46);
        for (var i = 0; i < buttons.Length; i++)
            buttons[i].SetBounds(Width - width * (buttons.Length - i), 0, width, Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tips.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (backgroundOpacity < 100 && WindowAppearance.PaintWithAlpha(e.Graphics, ClientRectangle,
            graphics => graphics.Clear(Color.FromArgb(backgroundOpacity * 255 / 100, palette.Header)))) return;
        e.Graphics.Clear(palette.Header);
    }

    protected override void WndProc(ref Message message)
    {
        // Blank caption space delegates hit testing to the top-level custom frame.
        if (message.Msg == 0x0084) { message.Result = (IntPtr)(-1); return; } // HTTRANSPARENT
        base.WndProc(ref message);
    }
}

internal enum CaptionGlyph { Theme, Minimize, Maximize, Restore, Close }

internal sealed class CaptionButton : Button
{
    private WindowPalette palette;
    private bool hover;
    private int backgroundOpacity = 100;
    public CaptionGlyph Glyph { get; set; }

    public CaptionButton(string accessibleName, CaptionGlyph glyph, WindowPalette palette, Action action)
    {
        this.palette = palette; Glyph = glyph;
        AccessibleName = accessibleName; TabStop = false;
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Click += (_, _) => action();
    }

    public void ApplyPalette(WindowPalette value) { palette = value; Invalidate(); }
    public void SetBackgroundOpacity(int opacity)
    {
        backgroundOpacity = opacity;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, opacity == 100);
        Invalidate();
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var background = hover ? Glyph == CaptionGlyph.Close ? Color.FromArgb(200, 51, 64) : palette.Border : palette.Header;
        if (backgroundOpacity < 100 && WindowAppearance.PaintWithAlpha(e.Graphics, ClientRectangle,
            graphics => PaintGlyph(graphics, hover ? background : Color.FromArgb(backgroundOpacity * 255 / 100, background)))) return;
        PaintGlyph(e.Graphics, background);
    }

    private void PaintGlyph(Graphics graphics, Color background)
    {
        graphics.Clear(background);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        var size = 10 * scale; var x = (Width - size) / 2; var y = (Height - size) / 2;
        using var pen = new Pen(hover && Glyph == CaptionGlyph.Close ? Color.White : palette.Text, Math.Max(1, scale));
        switch (Glyph)
        {
            case CaptionGlyph.Theme: DrawPalette(graphics, pen, scale); break;
            case CaptionGlyph.Minimize: graphics.DrawLine(pen, x, y + size / 2, x + size, y + size / 2); break;
            case CaptionGlyph.Maximize: graphics.DrawRectangle(pen, x, y, size, size); break;
            case CaptionGlyph.Restore:
                graphics.DrawLines(pen, new PointF[] { new(x + 3 * scale, y + 2 * scale), new(x + 3 * scale, y), new(x + size, y), new(x + size, y + size - 3 * scale) });
                graphics.DrawRectangle(pen, x, y + 3 * scale, size - 3 * scale, size - 3 * scale);
                break;
            default:
                graphics.DrawLine(pen, x, y, x + size, y + size);
                graphics.DrawLine(pen, x + size, y, x, y + size);
                break;
        }
        if (Focused && ShowFocusCues)
        {
            var inset = LogicalToDeviceUnits(3);
            using var focus = new Pen(palette.Text, Math.Max(1, scale));
            graphics.DrawRectangle(focus, inset, inset, Width - 2 * inset - 1, Height - 2 * inset - 1);
        }
    }

    private void DrawPalette(Graphics graphics, Pen pen, float scale)
    {
        var originX = (Width - 14 * scale) / 2;
        var originY = (Height - 14 * scale) / 2;
        PointF At(float x, float y) => new(originX + x * scale, originY + y * scale);
        using var shape = new GraphicsPath();
        shape.AddBezier(At(7, 1), At(11, 1), At(13, 4), At(13, 7));
        shape.AddBezier(At(13, 7), At(13, 10), At(9, 8), At(9, 10));
        shape.AddBezier(At(9, 10), At(9, 11), At(11, 13), At(7, 13));
        shape.AddBezier(At(7, 13), At(-1, 13), At(-1, 1), At(7, 1));
        shape.CloseFigure();
        graphics.DrawPath(pen, shape);
        using var dot = new SolidBrush(pen.Color);
        foreach (var center in new[] { At(4, 5), At(7, 3.8f), At(10, 5.2f) })
            graphics.FillEllipse(dot, center.X - .7f * scale, center.Y - .7f * scale, 1.4f * scale, 1.4f * scale);
    }
}
