using System.Windows;
using System.Windows.Controls;

namespace Yptv;

// One line of text, asked for in a small modal (rename).
public partial class PromptDialog : Window
{
    public string Value => Input.Text.Trim();

    public PromptDialog(string title, string label, string initial)
    {
        InitializeComponent();
        Title = $"{title} - {AppInfo.Name}";
        SourceInitialized += (_, _) => TitleBar.Apply(this);
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
        Label.Text = label;
        Input.Text = initial;
    }

    void Input_TextChanged(object sender, TextChangedEventArgs e) => Ok.IsEnabled = Input.Text.Trim().Length > 0;

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
