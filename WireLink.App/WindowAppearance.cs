using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WireLink.App;

internal sealed record WindowPalette(Color Background, Color Header, Color Sidebar, Color Card,
    Color Border, Color Text, Color Muted, Color Accent)
{
    public static WindowPalette For(bool dark) => dark
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
