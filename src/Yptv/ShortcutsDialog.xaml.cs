using System.Windows;

namespace Yptv;

// Cheat sheet, generated from the shared table in Hotkeys.cs.
public partial class ShortcutsDialog : Window
{
    public ShortcutsDialog()
    {
        InitializeComponent();
        Title = $"Shortcuts - {AppInfo.Name}";
        SourceInitialized += (_, _) => TitleBar.Apply(this);
        Rows.ItemsSource = Hotkeys.All.Select(h => new { h.Keys, h.Action }).ToList();
    }
}
