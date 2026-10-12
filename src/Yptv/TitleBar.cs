using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Yptv;

// Native title bar, tinted to the active theme. Windows 11 honours the colours; older builds only get the dark/light switch.
static class TitleBar
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    const int UseImmersiveDarkMode = 20, BorderColor = 34, CaptionColor = 35, TextColor = 36;

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return; // not shown yet: SourceInitialized applies it
        var (caption, border, text) = Themes.TitleColors;
        Set(hwnd, UseImmersiveDarkMode, Themes.IsDark ? 1 : 0);
        Set(hwnd, CaptionColor, caption);
        Set(hwnd, BorderColor, border);
        Set(hwnd, TextColor, text);
    }

    static void Set(IntPtr hwnd, int attribute, int value) => DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
}
