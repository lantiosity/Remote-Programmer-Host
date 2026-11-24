using System.Runtime.InteropServices;

namespace WireLink.App;

// The OS keeps a real window title for taskbar/Alt+Tab; we draw no caption text.
public class ChromeForm : Form
{
    private const int ResizeBorder = 6;
    private (Rectangle Bounds, int Direction, int Dpi)? resizeStart;

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

    private protected void BeginEdgeResize(int direction)
    {
        if (WindowState == FormWindowState.Normal && direction is >= 1 and <= 8)
            resizeStart = (Bounds, direction, DeviceDpi);
    }

    private protected void MoveEdgeResize(int x, int y, bool finished)
    {
        if (resizeStart is not { } start) return;
        if (finished) resizeStart = null;
        if (WindowState != FormWindowState.Normal) return;
        var dx = (int)Math.Round(Math.Clamp(x, -32768, 32768) * start.Dpi / 96d);
        var dy = (int)Math.Round(Math.Clamp(y, -32768, 32768) * start.Dpi / 96d);
        var area = start.Bounds;
        var left = start.Direction is 1 or 4 or 7 ? Math.Min(area.Left + dx, area.Right - MinimumSize.Width) : area.Left;
        var right = start.Direction is 2 or 5 or 8 ? Math.Max(area.Right + dx, area.Left + MinimumSize.Width) : area.Right;
        var top = start.Direction is 3 or 4 or 5 ? Math.Min(area.Top + dy, area.Bottom - MinimumSize.Height) : area.Top;
        var bottom = start.Direction is 6 or 7 or 8 ? Math.Max(area.Bottom + dy, area.Top + MinimumSize.Height) : area.Bottom;
        Bounds = Rectangle.FromLTRB(left, top, right, bottom);
    }

    private int ResizeHit(Point point)
    {
        if (WindowState != FormWindowState.Normal) return 0;
        var edge = LogicalToDeviceUnits(ResizeBorder);
        var left = point.X < edge;
        var right = point.X >= ClientSize.Width - edge;
        var top = point.Y < edge;
        var bottom = point.Y >= ClientSize.Height - edge;
        return top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17
            : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 0;
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
            var hit = ResizeHit(point);
            if (hit != 0) message.Result = (IntPtr)hit;
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint(int x, int y) { public int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo
    {
        public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }
}
