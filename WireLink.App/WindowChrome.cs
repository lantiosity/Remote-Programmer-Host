using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WireLink.App;

// The OS keeps a real window title for taskbar/Alt+Tab; we draw no caption text.
public class ChromeForm : Form
{
    private const int ResizeBorder = 6;
    private CaptionBar? caption;

    private protected void InstallChrome(WindowPalette palette)
    {
        FormBorderStyle = FormBorderStyle.None;
        BackColor = palette.Header;
        Padding = new Padding(ResizeBorder);
        caption = new CaptionBar(this, palette) { Dock = DockStyle.Top, Height = 32 };
        Controls.Add(caption);
        caption.SendToBack(); // Dock the top strip before the fill content.
    }

    private protected void ApplyChromePalette(WindowPalette palette)
    {
        BackColor = palette.Header;
        caption?.ApplyPalette(palette);
    }

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
    private readonly CaptionButton minimize, maximize, close;
    private WindowPalette palette;

    public CaptionBar(Form owner, WindowPalette palette)
    {
        this.owner = owner; this.palette = palette;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AccessibleName = "窗口控制栏";
        minimize = new("最小化", CaptionGlyph.Minimize, palette, () => owner.WindowState = FormWindowState.Minimized);
        maximize = new("最大化", CaptionGlyph.Maximize, palette, () => owner.WindowState = owner.WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal : FormWindowState.Maximized);
        close = new("关闭", CaptionGlyph.Close, palette, owner.Close);
        Controls.AddRange([minimize, maximize, close]);
    }

    public void ApplyPalette(WindowPalette value)
    {
        palette = value;
        foreach (CaptionButton button in Controls) button.ApplyPalette(value);
        Invalidate();
    }

    public void RefreshWindowState()
    {
        maximize.Glyph = owner.WindowState == FormWindowState.Maximized ? CaptionGlyph.Restore : CaptionGlyph.Maximize;
        maximize.AccessibleName = owner.WindowState == FormWindowState.Maximized ? "还原窗口" : "最大化";
        maximize.Invalidate();
    }

    public bool IsDragPoint(Point parentPoint) => Bounds.Contains(parentPoint) && parentPoint.X < Left + minimize.Left;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        var width = (int)Math.Round(46 * DeviceDpi / 96f);
        minimize.SetBounds(Width - width * 3, 0, width, Height);
        maximize.SetBounds(Width - width * 2, 0, width, Height);
        close.SetBounds(Width - width, 0, width, Height);
    }

    protected override void OnPaint(PaintEventArgs e) => e.Graphics.Clear(palette.Header);

    protected override void WndProc(ref Message message)
    {
        // Blank caption space delegates hit testing to the top-level custom frame.
        if (message.Msg == 0x0084) { message.Result = (IntPtr)(-1); return; } // HTTRANSPARENT
        base.WndProc(ref message);
    }
}

internal enum CaptionGlyph { Minimize, Maximize, Restore, Close }

internal sealed class CaptionButton : Button
{
    private WindowPalette palette;
    private bool hover;
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
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var background = hover ? Glyph == CaptionGlyph.Close ? Color.FromArgb(200, 51, 64) : palette.Border : palette.Header;
        e.Graphics.Clear(background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        var size = 10 * scale; var x = (Width - size) / 2; var y = (Height - size) / 2;
        using var pen = new Pen(hover && Glyph == CaptionGlyph.Close ? Color.White : palette.Text, Math.Max(1, scale));
        switch (Glyph)
        {
            case CaptionGlyph.Minimize: e.Graphics.DrawLine(pen, x, y + size / 2, x + size, y + size / 2); break;
            case CaptionGlyph.Maximize: e.Graphics.DrawRectangle(pen, x, y, size, size); break;
            case CaptionGlyph.Restore:
                e.Graphics.DrawLines(pen, new PointF[] { new(x + 3 * scale, y + 2 * scale), new(x + 3 * scale, y), new(x + size, y), new(x + size, y + size - 3 * scale) });
                e.Graphics.DrawRectangle(pen, x, y + 3 * scale, size - 3 * scale, size - 3 * scale);
                break;
            default:
                e.Graphics.DrawLine(pen, x, y, x + size, y + size);
                e.Graphics.DrawLine(pen, x + size, y, x, y + size);
                break;
        }
    }
}
