using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace Yptv;

// The one settings dialog. Every control writes straight into Settings.Current (saved soon after), no OK/Cancel.
public partial class SettingsDialog : Window
{
    static readonly (string Value, string Text)[] StartupChoices =
        [("Resume", "Resume the last channel"), ("List", "Select it, don't play"), ("None", "Nothing selected")];

    readonly MainWindow _host;
    readonly bool _loading = true;

    public SettingsDialog(MainWindow host)
    {
        InitializeComponent();
        Title = $"Settings - {AppInfo.Name}";
        SourceInitialized += (_, _) => TitleBar.Apply(this);
        _host = host;
        var s = Settings.Current;

        ThemeBox.ItemsSource = Themes.Names.Select(n => n == "Amoled" ? "AMOLED" : n == "System" ? "System (follow Windows)" : n).ToList();
        ThemeBox.SelectedIndex = Math.Max(0, Array.IndexOf(Themes.Names, s.Theme));
        StartupBox.ItemsSource = StartupChoices.Select(c => c.Text).ToList();
        StartupBox.SelectedIndex = Math.Max(0, Array.FindIndex(StartupChoices, c => c.Value == s.Startup));
        AutoBox.ItemsSource = PlaylistsDialog.AutoChoices.Select(c => c.Text).ToList();
        AutoBox.SelectedIndex = Math.Max(0, Array.FindIndex(PlaylistsDialog.AutoChoices, c => c.Value == s.AutoRefresh));
        HwCheck.IsChecked = s.HwDecoding;
        BufferBox.Text = s.BufferMs.ToString();
        AgentBox.Text = s.UserAgent ?? "";
        About.Text = $"{AppInfo.Name} {Diagnostics.Version} | LibVLC {host.LibVlcVersion ?? "-"}";
        _loading = false;
        ShowSizes();
        Closing += (_, _) => // a field still being edited when the dialog closes
        {
            Buffer_Commit(this, new RoutedEventArgs());
            Agent_Commit(this, new RoutedEventArgs());
        };
    }

    // Sizes are measured when the dialog opens (and after a clear), not kept up to date.
    void ShowSizes()
    {
        LiveSize.Text = Caches.Format(Caches.Size(Caches.Kind.Live));
        VodSize.Text = Caches.Format(Caches.Size(Caches.Kind.Vod));
        LogoSize.Text = Caches.Format(Caches.Size(Caches.Kind.Logos));
    }

    void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedIndex < 0) return;
        Themes.Apply(Themes.Names[ThemeBox.SelectedIndex]);
        Settings.SaveSoon();
    }

    void Startup_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || StartupBox.SelectedIndex < 0) return;
        Settings.Current.Startup = StartupChoices[StartupBox.SelectedIndex].Value;
        Settings.SaveSoon();
    }

    void Auto_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AutoBox.SelectedIndex < 0) return;
        Settings.Current.AutoRefresh = PlaylistsDialog.AutoChoices[AutoBox.SelectedIndex].Value;
        Settings.SaveSoon();
        _host.ScheduleAutoRefresh();
    }

    void Hw_Click(object sender, RoutedEventArgs e)
    {
        Settings.Current.HwDecoding = HwCheck.IsChecked == true;
        Settings.SaveSoon();
    }

    void Buffer_Commit(object sender, RoutedEventArgs e)
    {
        var ms = int.TryParse(BufferBox.Text.Trim(), out var value) ? Math.Clamp(value, 100, 5000) : Settings.Current.BufferMs;
        BufferBox.Text = ms.ToString();
        Settings.Current.BufferMs = ms;
        Settings.SaveSoon();
    }

    void Agent_Commit(object sender, RoutedEventArgs e)
    {
        var text = AgentBox.Text.Trim();
        Settings.Current.UserAgent = text.Length == 0 ? null : text;
        Settings.SaveSoon();
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        var kind = Enum.Parse<Caches.Kind>((string)((Button)sender).Tag);
        Caches.Clear(kind);
        ShowSizes();
        CacheNote.Text = kind == Caches.Kind.Logos
            ? "Logos are downloaded again as you scroll."
            : "Cleared. The list is loaded from the provider on the next start, or use Refresh now in the playlist manager.";
    }

    void Shortcuts_Click(object sender, RoutedEventArgs e) => new ShortcutsDialog { Owner = this }.ShowDialog();

    void Logs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Log.Dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.Dir}\"") { UseShellExecute = true });
    }

    void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Diagnostics.Build(_host.LibVlcVersion));
            CacheNote.Text = "Diagnostics copied. Addresses and file paths are replaced by <url> and <path>.";
        }
        catch (COMException)
        {
            CacheNote.Text = "The clipboard is busy. Try again.";
        }
    }
}
