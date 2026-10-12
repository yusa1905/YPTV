using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Yptv;

// Add a playlist from an M3U URL / file or an Xtream login. The source is checked before it is saved.
public partial class AddPlaylistDialog : Window
{
    CancellationTokenSource? _check;

    public PlaylistEntry? Result { get; private set; }

    public AddPlaylistDialog(string? prefill)
    {
        InitializeComponent();
        Title = $"Add playlist - {AppInfo.Name}";
        SourceInitialized += (_, _) => TitleBar.Apply(this);
        Loaded += (_, _) => { SourceBox.Focus(); SourceBox.SelectAll(); };
        Closed += (_, _) => _check?.Cancel();
        SourceBox.Text = prefill ?? "";
        UpdateOk();
    }

    bool IsXtream => XtreamTab.IsChecked == true;

    void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (M3uPanel == null) return; // fires while the XAML loads
        M3uPanel.Visibility = IsXtream ? Visibility.Collapsed : Visibility.Visible;
        XtreamPanel.Visibility = IsXtream ? Visibility.Visible : Visibility.Collapsed;
        Status.Text = "";
        (IsXtream ? HostBox : SourceBox).Focus();
        UpdateOk();
    }

    void Field_Changed(object sender, RoutedEventArgs e) => UpdateOk();

    void UpdateOk() => Ok.IsEnabled = _check == null && (IsXtream
        ? HostBox.Text.Trim().Length > 0 && UserBox.Text.Trim().Length > 0 && PassBox.Password.Length > 0
        : SourceBox.Text.Trim().Length > 0);

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Playlists (*.m3u;*.m3u8)|*.m3u;*.m3u8|All files (*.*)|*.*" };
        if (dlg.ShowDialog(this) == true) SourceBox.Text = dlg.FileName;
    }

    async void Ok_Click(object sender, RoutedEventArgs e)
    {
        var cts = _check = new CancellationTokenSource();
        Ok.IsEnabled = false;
        Status.Foreground = (System.Windows.Media.Brush)FindResource("Fg2");
        Status.Text = "Checking…";
        try
        {
            var (entry, error) = IsXtream ? await CheckXtreamAsync(cts.Token) : await CheckM3uAsync(cts.Token);
            if (cts.IsCancellationRequested) return;
            if (entry != null)
            {
                Result = entry;
                DialogResult = true;
                return;
            }
            Status.Foreground = (System.Windows.Media.Brush)FindResource("Err");
            Status.Text = error;
        }
        finally
        {
            if (_check == cts) _check = null;
            UpdateOk();
        }
    }

    async Task<(PlaylistEntry?, string?)> CheckM3uAsync(CancellationToken ct)
    {
        var source = SourceBox.Text.Trim().Trim('"'); // pasted paths often come quoted
        var error = await PlaylistProbe.CheckM3uAsync(source, ct);
        return error != null ? (null, error) : (Settings.Current.AddSource(source, NameBox.Text, "m3u"), null);
    }

    async Task<(PlaylistEntry?, string?)> CheckXtreamAsync(CancellationToken ct)
    {
        if (!TryReadLogin(out var login, out var problem)) return (null, problem);
        var info = await Xtream.FetchAsync(login, ct);
        if (info?.Status == "Rejected") return (null, "The server refused the login. Check username and password.");
        var link = login.LinkFor(info?.Formats);
        if (info == null && await PlaylistProbe.CheckM3uAsync(link, ct) is { } error) return (null, error); // no account API: the link must work
        var entry = Settings.Current.AddSource(link, NameBox.Text, "xtream");
        if (info != null) Xtream.SaveCached(entry.Id, info);
        return (entry, null);
    }

    bool TryReadLogin(out XtreamLogin login, out string? problem)
    {
        login = null!;
        var host = HostBox.Text.Trim().TrimEnd('/');
        if (!host.Contains("://")) host = "http://" + host;
        if (!Uri.TryCreate(host, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0)
        {
            problem = "The server address isn't valid. Use a name like provider.example or https://provider.example.";
            return false;
        }
        int? port = uri.IsDefaultPort ? null : uri.Port;
        var portText = PortBox.Text.Trim();
        if (portText.Length > 0)
        {
            if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var p) || p is < 1 or > 65535)
            {
                problem = "The port must be a number from 1 to 65535.";
                return false;
            }
            port = p;
        }
        login = new XtreamLogin(uri.Scheme, uri.Host, port, UserBox.Text.Trim(), PassBox.Password);
        problem = null;
        return true;
    }
}
