using System.Runtime.InteropServices;

namespace WireLink.App;

internal readonly record struct GlassRegion(double X, double Y, double Width, double Height, double Radius);

// WebView2's CSS filters sample the document. Passive DWM surfaces directly below the workbench
// also blur the real desktop where the app's own background is clear.
internal sealed class RegionBackdrop : IDisposable
{
    private GlassRegion[] regions = [];
    private readonly List<Pane> panes = [];

    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) && !SystemInformation.HighContrast;

    public void UpdateMask(GlassRegion[] areas) => regions = areas;

    public void Present(IntPtr owner, Rectangle bounds, double dpiScale, bool dark)
    {
        int Scale(double value) => (int)Math.Round(value * dpiScale);
        var count = 0;
        foreach (var area in regions)
        {
            var rect = Rectangle.FromLTRB(bounds.Left + Scale(area.X), bounds.Top + Scale(area.Y),
                bounds.Left + Scale(area.X + area.Width), bounds.Top + Scale(area.Y + area.Height));
            rect.Intersect(bounds);
            if (rect.Width <= 0 || rect.Height <= 0) continue;
            if (count == panes.Count) panes.Add(new Pane());
            panes[count++].Present(owner, rect, area.Radius > 0, dark);
        }
        for (var i = count; i < panes.Count; i++) panes[i].Hide();
    }

    public void Hide()
    {
        foreach (var pane in panes) pane.Hide();
    }

    public void Dispose()
    {
        foreach (var pane in panes) pane.DestroyHandle();
        panes.Clear();
    }

    private sealed class Pane : NativeWindow
    {
        private bool visible;
        private (bool Dark, bool Rounded)? style;

        public Pane()
        {
            CreateHandle(new CreateParams
            {
                Style = unchecked((int)0x80040000), // WS_POPUP | WS_THICKFRAME
                ExStyle = 0x080000A0 // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT
            });
            WindowAppearance.EnableDesktopBackdrop(Handle);
            WindowAppearance.SetDesktopMaterial(Handle, WindowAppearance.DesktopMaterial.Clear);
        }

        public void Present(IntPtr owner, Rectangle bounds, bool rounded, bool dark)
        {
            visible = SetWindowPos(Handle, owner, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0050); // SHOWWINDOW | NOACTIVATE
            if (style != (dark, rounded))
            {
                WindowAppearance.ApplyFrame(Handle, dark, rounded);
                if (!SetBackgroundAccent(Handle, dark))
                    WindowAppearance.SetDesktopMaterial(Handle, WindowAppearance.DesktopMaterial.Acrylic);
                style = (dark, rounded);
            }
        }

        public void Hide()
        {
            if (!visible) return;
            ShowWindow(Handle, 0);
            visible = false;
        }

        protected override void WndProc(ref Message message)
        {
            switch (message.Msg)
            {
                case 0x0083: // WM_NCCALCSIZE
                case 0x0085: // WM_NCPAINT
                    message.Result = IntPtr.Zero;
                    return;
                case 0x0084: // WM_NCHITTEST
                    message.Result = new IntPtr(-1); // HTTRANSPARENT
                    return;
                default:
                    base.WndProc(ref message);
                    break;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy(uint tint)
    {
        public int State = 4; // ACCENT_ENABLE_ACRYLICBLURBEHIND
        public int Flags = 0;
        public uint GradientColor = tint;
        public int AnimationId = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttribute(IntPtr data, int size)
    {
        public int Attribute = 19; // WCA_ACCENT_POLICY
        public IntPtr Data = data;
        public nuint Size = (nuint)size;
    }

    private static bool SetBackgroundAccent(IntPtr window, bool dark)
    {
        var policy = new AccentPolicy(dark ? 0x281B120Du : 0x28FFFFFFu);
        var size = Marshal.SizeOf<AccentPolicy>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, buffer, false);
            var attribute = new WindowCompositionAttribute(buffer, size);
            return SetWindowCompositionAttribute(window, ref attribute);
        }
        catch (EntryPointNotFoundException) { return false; }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowCompositionAttribute(IntPtr window, ref WindowCompositionAttribute attribute);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern int ShowWindow(IntPtr window, int command);
}
