using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Yptv;

// Borderless, transparent window owned by the main window and parked exactly over the video host.
// WPF content can't be drawn over a native child window (airspace), a separate window can.
sealed class OverlayWindow : Window
{
    public OverlayWindow(UIElement content, bool debug)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = debug ? new SolidColorBrush(Color.FromArgb(0x50, 0, 0xFF, 0xFF)) : Brushes.Transparent; // cyan when debugging
        Content = content;
    }

    // Pixels, screen coordinates.
    public void MoveTo(int x, int y, int width, int height)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, NoZOrder | NoActivate);
    }

    const uint NoZOrder = 0x0004, NoActivate = 0x0010;

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
}
