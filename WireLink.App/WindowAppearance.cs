using System.Runtime.InteropServices;
using Microsoft.Win32;
using WireLink.Core;

namespace WireLink.App;

internal sealed record WindowPalette(Color Background, Color Header, Color Sidebar, Color Card,
    Color Border, Color Text, Color Muted, Color Accent)
{
    public Color Primary { get; init; } = Color.FromArgb(8, 123, 121);

    public static WindowPalette For(bool dark, ThemeColors? custom = null)
    {
        var palette = Default(dark);
        if (ThemeColors.Validated(custom) is not { } colors) return palette;
        var accent = ColorTranslator.FromHtml(colors.Accent);
        var background = ColorTranslator.FromHtml(colors.Background);
        var surface = ColorTranslator.FromHtml(colors.Surface);
        return palette with
        {
            Background = Blend(palette.Background, background, 18),
            Header = Blend(palette.Header, surface, 14),
            Sidebar = Blend(palette.Sidebar, surface, 14),
            Card = Blend(palette.Card, surface, 14),
            Accent = accent,
            Primary = accent
        };
    }

    private static Color Blend(Color basis, Color tint, int amount)
    {
        int Mix(int a, int b) => (a * (100 - amount) + b * amount + 50) / 100;
        return Color.FromArgb(Mix(basis.R, tint.R), Mix(basis.G, tint.G), Mix(basis.B, tint.B));
    }

    public static Color OnColor(Color background)
    {
        static double Channel(byte channel)
        {
            var value = channel / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        var luminance = .2126 * Channel(background.R) + .7152 * Channel(background.G) + .0722 * Channel(background.B);
        return luminance > .179 ? Color.Black : Color.White;
    }

    private static WindowPalette Default(bool dark) => dark
        ? new(Color.FromArgb(17, 25, 37), Color.FromArgb(20, 30, 43), Color.FromArgb(21, 31, 45),
            Color.FromArgb(25, 36, 51), Color.FromArgb(41, 53, 72), Color.FromArgb(217, 227, 240),
            Color.FromArgb(132, 147, 168), Color.FromArgb(73, 194, 171))
        : new(Color.FromArgb(245, 247, 251), Color.FromArgb(252, 253, 255), Color.White,
            Color.White, Color.FromArgb(229, 234, 240), Color.FromArgb(25, 38, 55),
            Color.FromArgb(122, 135, 152), Color.FromArgb(8, 127, 128));
}

internal static class WindowAppearance
{
    internal enum DesktopMaterial { Solid, Clear, Acrylic }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr handle, ref FrameMargins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(IntPtr handle, ref BlurBehind blur);

    [StructLayout(LayoutKind.Sequential)]
    private struct BlurBehind(bool enabled)
    {
        public uint Flags = 1; // DWM_BB_ENABLE
        public int Enabled = enabled ? 1 : 0;
        public IntPtr Region = IntPtr.Zero;
        public int TransitionOnMaximized = 0;
    }

    [DllImport("uxtheme.dll")]
    private static extern IntPtr BeginBufferedPaint(IntPtr dc, ref PaintRect rect, int format,
        IntPtr parameters, out IntPtr bufferDc);

    [DllImport("uxtheme.dll")]
    private static extern int EndBufferedPaint(IntPtr buffer, [MarshalAs(UnmanagedType.Bool)] bool update);

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintRect(Rectangle bounds)
    {
        public int Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameMargins(int inset)
    {
        public int Left = inset, Right = inset, Top = inset, Bottom = inset;
    }

    public static bool SupportsDesktopBackdrop => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
        && !SystemInformation.HighContrast;

    public static bool EnableDesktopBackdrop(IntPtr handle)
    {
        if (!SupportsDesktopBackdrop) return false;
        var margins = new FrameMargins(-1);
        return DwmExtendFrameIntoClientArea(handle, ref margins) >= 0;
    }

    public static void SetDesktopMaterial(IntPtr handle, DesktopMaterial material)
    {
        var acrylic = material == DesktopMaterial.Acrylic && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);
        // On Windows 8+ this enables per-pixel composition without applying a blur.
        var blur = new BlurBehind(material != DesktopMaterial.Solid);
        DwmEnableBlurBehindWindow(handle, ref blur);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            var backdrop = acrylic ? 3 : 1; // DWMSBT_TRANSIENTWINDOW (Desktop Acrylic) / DWMSBT_NONE.
            DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int));
        }
    }

    public static bool PaintWithAlpha(Graphics target, Rectangle bounds, Action<Graphics> paint)
    {
        var dc = target.GetHdc();
        try
        {
            var rect = new PaintRect(bounds);
            var buffer = BeginBufferedPaint(dc, ref rect, 2, IntPtr.Zero, out var bufferDc); // BPBF_TOPDOWNDIB
            if (buffer == IntPtr.Zero) return false;
            try
            {
                using var graphics = Graphics.FromHdc(bufferDc);
                paint(graphics);
            }
            finally { EndBufferedPaint(buffer, true); }
            return true;
        }
        finally { target.ReleaseHdc(dc); }
    }

    public static bool IsDark(string? preference)
    {
        if (preference == "dark") return true;
        if (preference == "light") return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        { return false; }
    }

    public static void ApplyFrame(IntPtr handle, bool dark, WindowPalette palette)
    {
        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref value, sizeof(int));
        value = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE: no focus-dependent DWM outline.
        DwmSetWindowAttribute(handle, 34, ref value, sizeof(int));
        value = 2; // Rounded corners where supported; no OS caption is rendered.
        DwmSetWindowAttribute(handle, 33, ref value, sizeof(int));
    }
}
