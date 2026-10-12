using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Yptv;

public sealed class GroupEntry(GroupInfo info, bool visible) : INotifyPropertyChanged
{
    static readonly PropertyChangedEventArgs VisibleChanged = new(nameof(Visible));
    bool _visible = visible;

    public event PropertyChangedEventHandler? PropertyChanged;

    public GroupInfo Info { get; } = info;
    public string Name => Info.Label;
    public string CountText => Info.Count.ToString("N0");

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            PropertyChanged?.Invoke(this, VisibleChanged);
        }
    }
}

// Check the groups to show; unchecked ones are hidden from the list, search and zapping.
public partial class GroupsDialog : Window
{
    readonly List<GroupEntry> _all;
    List<GroupEntry> _shown = []; // entries after the filter box
    bool _bulk; // suppress per-entry summary updates

    public HashSet<string> Hidden { get; private set; } = [];

    public GroupsDialog(List<GroupInfo> infos, HashSet<string> hidden)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => TitleBar.Apply(this);
        Loaded += (_, _) => FilterBox.Focus();

        _all = infos.Select(i => new GroupEntry(i, !hidden.Contains(i.Name))).ToList();
        foreach (var entry in _all) entry.PropertyChanged += (_, _) => { if (!_bulk) UpdateSummary(); };
        ApplyFilter();
    }
    void Filter_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    void ApplyFilter()
    {
        var q = FilterBox.Text.Trim();
        _shown = q.Length == 0 ? _all : _all.Where(e => e.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        List.ItemsSource = _shown;
        UpdateSummary();
    }

    // Select all / none / invert act on the groups the filter box leaves visible.
    void All_Click(object sender, RoutedEventArgs e) => SetShown(_ => true);
    void None_Click(object sender, RoutedEventArgs e) => SetShown(_ => false);
    void Invert_Click(object sender, RoutedEventArgs e) => SetShown(entry => !entry.Visible);

    void SetShown(Func<GroupEntry, bool> value)
    {
        _bulk = true;
        foreach (var entry in _shown) entry.Visible = value(entry);
        _bulk = false;
        UpdateSummary();
    }

    void UpdateSummary()
    {
        int on = _all.Count(e => e.Visible);
        Summary.Text = $"{on:N0} of {_all.Count:N0} groups shown";
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        Hidden = _all.Where(x => !x.Visible).Select(x => x.Info.Name).ToHashSet();
        DialogResult = true;
    }
}

