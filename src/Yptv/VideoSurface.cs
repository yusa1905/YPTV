using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using LibVLCSharp.Shared;

namespace Yptv;

// The video area: a native child window whose own class paints BLACK (LibVLCSharp.WPF's host uses the system
// light gray, which showed as strips whenever LibVLC's video window lagged behind a resize) plus one owned,
// transparent overlay window that follows it. The content (VideoOverlay) lives in that overlay window.
// IPTV_DEBUG_BG=1 paints the host layer magenta and the overlay layer cyan to tell strips apart.
[ContentProperty(nameof(Content))]
sealed class VideoSurface : HwndHost
{
    static readonly bool Debug = Environment.GetEnvironmentVariable("IPTV_DEBUG_BG") == "1";
    static bool _classRegistered;
    const string ClassName = "YptvVideoHost";

    MediaPlayer? _player;
    IntPtr _hwnd;
    OverlayWindow? _overlay;
    Window? _owner;
    UIElement? _content;
    (int X, int Y, int W, int H) _lastRect;

    public UIElement? Content
    {
        get => _content;
        set => _content = value;
    }

    public MediaPlayer? MediaPlayer
    {
        get => _player;
        set
        {
            _player = value;
            if (_player != null && _hwnd != IntPtr.Zero) _player.Hwnd = _hwnd;
        }
    }

    public VideoSurface()
    {
        Loaded += (_, _) => AttachOverlay();
        Unloaded += (_, _) => DetachOverlay();
        SizeChanged += (_, _) => SyncOverlay();
        LayoutUpdated += (_, _) => SyncOverlay(); // cheap: only moves the window when the rectangle changed
    }

    // ---- overlay window -------------------------------------------------------------------------------------------

    void AttachOverlay()
    {
        if (_overlay != null || _content == null) return;
        _owner = Window.GetWindow(this);
        if (_owner == null) return;
        _overlay = new OverlayWindow(_content, Debug) { Owner = _owner };
        _owner.LocationChanged += OwnerChanged;
        _owner.SizeChanged += OwnerChanged;
        _owner.StateChanged += OwnerChanged;
        _overlay.Show();
        SyncOverlay();
    }

    void DetachOverlay()
    {
        if (_owner != null)
        {
            _owner.LocationChanged -= OwnerChanged;
            _owner.SizeChanged -= OwnerChanged;
            _owner.StateChanged -= OwnerChanged;
        }
        _overlay?.Close();
        _overlay = null;
    }

    void OwnerChanged(object? sender, EventArgs e) => SyncOverlay();

    void SyncOverlay()
    {
        if (_overlay == null || _hwnd == IntPtr.Zero || _owner is not { WindowState: not WindowState.Minimized }) return;
        if (!GetWindowRect(_hwnd, out var r)) return;
        var rect = (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        if (rect == _lastRect || rect.Item3 <= 0 || rect.Item4 <= 0) return;
        _lastRect = rect;
        _overlay.MoveTo(rect.Left, rect.Top, rect.Item3, rect.Item4);
    }

    // After fullscreen, theme switch and the like: lay out once, move the overlay, repaint. LibVLC's own video window
    // is left alone: resizing it behind LibVLC's back stretches the picture (MainWindow.ResyncVideo nudges LibVLC).
    public void Resync()
    {
        UpdateLayout();
        _lastRect = default;
        SyncOverlay();
        if (_hwnd == IntPtr.Zero) return;
        RedrawWindow(_hwnd, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwAllChildren | RdwUpdateNow | RdwErase);
    }

    // ---- native host ----------------------------------------------------------------------------------------------

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        EnsureClass();
        _hwnd = CreateWindowExW(0, ClassName, null, WsChild | WsVisible | WsClipChildren | WsClipSiblings,
            0, 0, 0, 0, hwndParent.Handle, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        if (_player != null) _player.Hwnd = _hwnd;
        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (_player != null) _player.Hwnd = IntPtr.Zero;
        DestroyWindow(hwnd.Handle);
        _hwnd = IntPtr.Zero;
    }

    static void EnsureClass()
    {
        if (_classRegistered) return;
        var user32 = GetModuleHandleW("user32.dll");
        var wc = new WndClassEx
        {
            cbSize = Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = GetProcAddress(user32, "DefWindowProcW"),
            hInstance = GetModuleHandleW(null),
            hCursor = LoadCursorW(IntPtr.Zero, (IntPtr)32512),
            hbrBackground = Debug ? CreateSolidBrush(0xFF00FF) : GetStockObject(4), // magenta, or BLACK_BRUSH
            lpszClassName = ClassName,
        };
        RegisterClassExW(ref wc);
        _classRegistered = true;
    }

    const int WsChild = 0x40000000, WsVisible = 0x10000000, WsClipChildren = 0x02000000, WsClipSiblings = 0x04000000;
    const uint SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;
    const uint RdwInvalidate = 0x1, RdwErase = 0x4, RdwUpdateNow = 0x100, RdwAllChildren = 0x80;

    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WndClassEx
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    delegate bool EnumChildProc(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassExW(ref WndClassEx wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowExW(int exStyle, string cls, string? name, int style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumChildProc proc, IntPtr param);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadCursorW(IntPtr instance, IntPtr name);
    [DllImport("gdi32.dll")] static extern IntPtr GetStockObject(int index);
    [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(uint colorRef);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
}
