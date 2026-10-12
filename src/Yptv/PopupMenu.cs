using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Yptv;

// One small popup list for toolbar buttons (theme, playback options). Built on click, nothing kept around.
static class PopupMenu
{
    public readonly record struct Item(string Text, bool Checked, Action? Click, bool Enabled = true);

    // The fullscreen deck stays up while a menu from it is open.
    public static bool IsOpen { get; private set; }

    public static void Show(FrameworkElement anchor, IEnumerable<Item> items, PlacementMode placement = PlacementMode.Top)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = placement, StaysOpen = false };
        foreach (var item in items)
        {
            var entry = new MenuItem { Header = item.Text, IsChecked = item.Checked, IsEnabled = item.Enabled && item.Click != null };
            if (item.Click is { } click) entry.Click += (_, _) => click();
            menu.Items.Add(entry);
        }
        menu.Opened += (_, _) => IsOpen = true;
        menu.Closed += (_, _) => IsOpen = false;
        menu.IsOpen = true;
    }
}
