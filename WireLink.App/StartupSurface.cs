using System.Drawing.Drawing2D;

namespace WireLink.App;

// A real Win32-painted first frame; it does not depend on the browser starting.
internal sealed class StartupSurface : Control
{
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 40 };
    private WindowPalette palette;
    private int frame;
    private bool painted;
    public Action? FirstPaint { get; set; }
    public string Status { get; set; } = "正在准备工作台…";

    public StartupSurface(WindowPalette palette)
    {
        this.palette = palette;
        Dock = DockStyle.Fill;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
        animation.Tick += (_, _) => { frame++; Invalidate(); };
        animation.Start();
        AccessibleName = "WireLink 启动画面";
    }

    public void ApplyPalette(WindowPalette value) { palette = value; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var scale = DeviceDpi / 96f;
        var g = e.Graphics;
        g.Clear(palette.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var sidebar = new SolidBrush(palette.Sidebar);
        using var header = new SolidBrush(palette.Header);
        using var card = new SolidBrush(palette.Card);
        using var border = new Pen(palette.Border);
        using var accent = new SolidBrush(palette.Accent);
        var side = 258 * scale;
        g.FillRectangle(sidebar, 0, 0, side, Height);
        g.DrawLine(border, side, 0, side, Height);
        g.FillRectangle(header, side + 1, 0, Width - side - 1, 64 * scale);
        g.DrawLine(border, side, 64 * scale, Width, 64 * scale);
        using var brandFont = new Font("Segoe UI", 19, FontStyle.Bold);
        using var titleFont = new Font("Microsoft YaHei UI", 17, FontStyle.Bold);
        using var detailFont = new Font("Microsoft YaHei UI", 10);
        TextRenderer.DrawText(g, "WireLink", brandFont, new Point((int)(28 * scale), (int)(29 * scale)), palette.Text);
        TextRenderer.DrawText(g, "远程烧录工作台", detailFont, new Point((int)(28 * scale), (int)(67 * scale)), palette.Muted);
        var center = side + (Width - side) / 2;
        var top = Height / 2f - 90 * scale;
        g.FillEllipse(accent, center - 23 * scale, top, 46 * scale, 46 * scale);
        TextRenderer.DrawText(g, "W", brandFont, new Rectangle((int)(center - 23 * scale), (int)top,
            (int)(46 * scale), (int)(46 * scale)), WindowPalette.OnColor(palette.Accent),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "WireLink", titleFont, new Rectangle((int)(side + 30 * scale), (int)(top + 65 * scale),
            (int)(Width - side - 60 * scale), (int)(40 * scale)), palette.Text, TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, Status, detailFont, new Rectangle((int)(side + 30 * scale), (int)(top + 111 * scale),
            (int)(Width - side - 60 * scale), (int)(32 * scale)), palette.Muted, TextFormatFlags.HorizontalCenter);
        var track = new RectangleF(center - 105 * scale, top + 163 * scale, 210 * scale, 3 * scale);
        using var trackBrush = new SolidBrush(palette.Border);
        g.FillRectangle(trackBrush, track);
        var offset = (frame % 70) / 69f * (track.Width - 52 * scale);
        g.FillRectangle(accent, track.X + offset, track.Y, 52 * scale, track.Height);
        if (!painted) { painted = true; FirstPaint?.Invoke(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) animation.Dispose();
        base.Dispose(disposing);
    }
}
