using System.Windows;

namespace Yptv;

// Xtream account info: the expiry segment in the status bar and the account panel.
public partial class MainWindow
{
    bool _accountBusy;

    // After a (cached or fresh) list arrives: is this an Xtream playlist? Show what we know about the account right away.
    void FindAccount(PlaylistEntry entry, List<Channel> channels)
    {
        var login = Xtream.Detect(entry.Source, channels.Count > 0 ? channels[0].Url : null);
        Xtream.Remember(entry.Id, login);
        bool xtream = login != null;
        if (entry.Xtream != xtream)
        {
            entry.Xtream = xtream;
            Settings.SaveSoon();
        }
        Status.SetExpiry(xtream && _entry == entry ? Xtream.LoadCached(entry.Id) : null);
    }

    // Once per playlist refresh, in the background. Silent when the panel doesn't answer.
    async Task RefreshAccountAsync(PlaylistEntry entry)
    {
        if (_accountBusy || Xtream.Login(entry.Id) is not { } login) return;
        _accountBusy = true;
        try
        {
            if (await Xtream.FetchAsync(login) is not { } info) return;
            Xtream.SaveCached(entry.Id, info);
            if (_entry == entry) Status.SetExpiry(info);
        }
        finally
        {
            _accountBusy = false;
        }
    }

    internal async Task ShowAccountAsync(PlaylistEntry entry, Window owner)
    {
        if (await Xtream.LoginAsync(entry) is not { } login)
        {
            MessageBox.Show(owner, "No Xtream login was found in this playlist.", AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new AccountDialog(entry, login, Xtream.LoadCached(entry.Id), info =>
        {
            if (_entry == entry) Status.SetExpiry(info);
        }) { Owner = owner };
        dialog.ShowDialog();
    }
}