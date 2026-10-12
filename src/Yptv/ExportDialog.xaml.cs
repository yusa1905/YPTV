using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Yptv;

// Save a playlist as .m3u from its caches: pick Live / Movies / Series / All and whether hidden groups are left out.
public partial class ExportDialog : Window
{
    readonly PlaylistEntry _entry;

    public ExportDialog(PlaylistEntry entry)
    {
        InitializeComponent();
        Title = $"Export - {AppInfo.Name}";
        SourceInitialized += (_, _) => TitleBar.Apply(this);
        _entry = entry;
    }

    async void Export_Click(object sender, RoutedEventArgs e)
    {
        ChannelKind[] kinds = AllRadio.IsChecked == true ? [ChannelKind.Live, ChannelKind.Movie, ChannelKind.Series]
            : MoviesRadio.IsChecked == true ? [ChannelKind.Movie]
            : SeriesRadio.IsChecked == true ? [ChannelKind.Series]
            : [ChannelKind.Live];
        if (!kinds.Any(k => File.Exists(PlaylistCache.FileFor(k, _entry.Id))))
        {
            Fail("Nothing to export yet. Refresh the playlist first.");
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = "Playlist (*.m3u)|*.m3u",
            DefaultExt = ".m3u",
            FileName = Safe(_entry.Name) + (kinds.Length == 1 ? "-" + kinds[0].ToString().ToLowerInvariant() : ""),
        };
        if (dialog.ShowDialog(this) != true) return;

        HashSet<string>? hidden = HiddenCheck.IsChecked == true
            ? new HashSet<string>(Settings.Current.PrefsFor(_entry.Id).Hidden, StringComparer.OrdinalIgnoreCase)
            : null;
        ExportButton.IsEnabled = false;
        Status.Foreground = (System.Windows.Media.Brush)FindResource("Fg2");
        Status.Text = "Exporting…";
        try
        {
            int count = await M3uExport.WriteAsync(_entry.Id, kinds, hidden, dialog.FileName, CancellationToken.None);
            Status.Text = $"Exported {count:N0} entries.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail("Couldn't write the file. Pick another place.");
        }
        finally
        {
            ExportButton.IsEnabled = true;
        }
    }

    void Fail(string text)
    {
        Status.Foreground = (System.Windows.Media.Brush)FindResource("Err");
        Status.Text = text;
    }

    static string Safe(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length == 0 ? "playlist" : name;
    }
}
