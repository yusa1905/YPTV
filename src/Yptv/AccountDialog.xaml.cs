using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace Yptv;

// Account info of an Xtream playlist: what the panel reports, plus the login (password masked until revealed).
public partial class AccountDialog : Window
{
    readonly PlaylistEntry _entry;
    readonly XtreamLogin _login;
    readonly Action<AccountInfo> _onUpdated;
    readonly Dictionary<string, TextBlock> _values = [];
    AccountInfo? _info;
    TextBlock _password = null!;
    Button _reveal = null!;
    bool _revealed;

    public AccountDialog(PlaylistEntry entry, XtreamLogin login, AccountInfo? info, Action<AccountInfo> onUpdated)
    {
        InitializeComponent();
        Title = $"Account - {AppInfo.Name}";
        SourceInitialized += (_, _) => TitleBar.Apply(this);
        _entry = entry;
        _login = login;
        _onUpdated = onUpdated;
        Heading.Text = entry.Name;
        foreach (var label in new[] { "Status", "Expires", "Trial", "Connections", "Created", "Server time", "Output formats", "Server", "Username" })
            AddRow(label);
        AddPasswordRow();
        _values["Server"].Text = login.Host;
        _values["Username"].Text = login.User;
        Show(info);
        Loaded += async (_, _) => await FetchAsync(); // fresh numbers every time the panel opens
    }

    void AddRow(string label)
    {
        Rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var name = new TextBlock { Text = label, Foreground = (System.Windows.Media.Brush)FindResource("Fg2"), Margin = new Thickness(0, 2, 0, 2) };
        var value = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
        Grid.SetRow(name, Rows.RowDefinitions.Count - 1);
        Grid.SetRow(value, Rows.RowDefinitions.Count - 1);
        Grid.SetColumn(value, 1);
        Rows.Children.Add(name);
        Rows.Children.Add(value);
        _values[label] = value;
    }

    void AddPasswordRow()
    {
        AddRow("Password");
        _password = _values["Password"];
        _reveal = new Button { Content = "Show", MinWidth = 0, Height = 20, Padding = new Thickness(8, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Right };
        _reveal.Click += (_, _) =>
        {
            _revealed = !_revealed;
            ShowPassword();
        };
        Grid.SetRow(_reveal, Rows.RowDefinitions.Count - 1);
        Grid.SetColumn(_reveal, 1);
        Rows.Children.Add(_reveal);
        ShowPassword();
    }

    void ShowPassword()
    {
        _password.Text = _revealed ? _login.Pass : new string('•', Math.Min(_login.Pass.Length, 12));
        _reveal.Content = _revealed ? "Hide" : "Show";
    }

    void Show(AccountInfo? info)
    {
        _info = info ?? _info;
        var i = _info;
        string Dash(string? s) => string.IsNullOrEmpty(s) ? "-" : s;
        _values["Status"].Text = Dash(i?.Status);
        _values["Expires"].Text = i == null ? "-" : i.Expires is > 0 and var e
            ? $"{FormatDate(e)}  ({Xtream.ExpirySummary(i).Text.Split('|').Last().Trim()})" : "Unlimited";
        _values["Trial"].Text = i?.Trial is { } t ? (t ? "Yes" : "No") : "-";
        _values["Connections"].Text = i == null ? "-" : $"{i.ActiveCons?.ToString() ?? "?"} active / {i.MaxCons?.ToString() ?? "?"} allowed";
        _values["Created"].Text = i?.Created is { } c and > 0 ? FormatDate(c) : "-";
        _values["Server time"].Text = i == null ? "-" : Dash(string.Join("  ", new[] { i.ServerTime, i.Timezone }.Where(s => !string.IsNullOrEmpty(s))));
        _values["Output formats"].Text = i is { Formats.Length: > 0 } ? string.Join(", ", i.Formats) : "-";
    }

    static string FormatDate(long unix) => DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    async Task FetchAsync()
    {
        RefreshButton.IsEnabled = false;
        RefreshButton.Content = "Refreshing…";
        var info = await Xtream.FetchAsync(_login);
        RefreshButton.Content = info == null ? "Refresh (no answer)" : "Refresh";
        RefreshButton.IsEnabled = true;
        if (info == null) return;
        Show(info);
        Xtream.SaveCached(_entry.Id, info);
        _onUpdated(info);
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await FetchAsync();

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_login.LinkFor(_info?.Formats));
            CopyButton.Content = "Copied";
        }
        catch (COMException)
        {
            CopyButton.Content = "Clipboard busy"; // locked by another app
        }
    }
}
