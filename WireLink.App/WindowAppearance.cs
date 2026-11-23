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
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

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
