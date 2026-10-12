using System.IO;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace Yptv;

// Dark / Light / Amoled / System. The active Theme*.xaml sits in the application resources; every brush is a
// DynamicResource, so swapping the dictionary recolors the open windows without a restart.
static class Themes
{
    public static readonly string[] Names = ["Dark", "Light", "Amoled", "System"];

    static ResourceDictionary? _active;
    static string _resolved = "Dark";

    public static string Resolved => _resolved;
    public static bool IsDark => _resolved != "Light";

    // Title bar colors per theme as 0x00BBGGRR: caption (Bg0), border (Line), text (Fg).
    public static (int Caption, int Border, int Text) TitleColors => _resolved switch
    {
        "Light" => (0xE9E8E4, 0x868476, 0x1A1912),
        "Amoled" => (0x000000, 0x262626, 0xE6E6E6),
        _ => (0x14130F, 0x0B0A07, 0xEAEAE3),
    };

    public static void Apply(string mode)
    {
        if (Array.IndexOf(Names, mode) < 0) mode = "Dark";
        // Only listen for Windows app-mode changes while the setting asks for it.
        SystemEvents.UserPreferenceChanged -= OnSystemChanged;
        if (mode == "System") SystemEvents.UserPreferenceChanged += OnSystemChanged;
        Settings.Current.Theme = mode;
        Load(mode == "System" ? SystemTheme() : mode);
    }

    static void Load(string name)
    {
        var resources = Application.Current.Resources.MergedDictionaries;
        if (_active != null) resources.Remove(_active);
        _active = new ResourceDictionary { Source = new Uri($"Theme{name}.xaml", UriKind.Relative) };
        resources.Add(_active);
        _resolved = name;
        foreach (Window window in Application.Current.Windows) TitleBar.Apply(window);
        (Application.Current.MainWindow as MainWindow)?.ResyncVideo();
    }

    static string SystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is 0 ? "Dark" : "Light";
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return "Dark";
        }
    }

    // Raised on SystemEvents' own thread.
    static void OnSystemChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)) return;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var name = SystemTheme();
            if (Settings.Current.Theme == "System" && name != _resolved) Load(name);
        });
    }
}
