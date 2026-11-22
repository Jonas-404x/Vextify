using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Vextify.Controls;
using Vextify.Core;

namespace Vextify.Views;

public partial class StartPage : Page
{
    private readonly Guardian _g = Guardian.Instance;

    private enum Action { None, Activate, Retry, DownloadSpotify, OpenSpotify, Reapply }
    private Action _primary = Action.None;
    private Action _secondary = Action.None;

    public StartPage()
    {
        InitializeComponent();
        Loaded += (_, _) => { _g.StatusChanged += OnStatus; _g.Refresh(); };
        Unloaded += (_, _) => _g.StatusChanged -= OnStatus;
    }

    private void OnStatus(Status s) => Dispatcher.Invoke(() => Render(s));

    /// <summary>One state, one orb, one sentence, one button. Everything else lives under Details.</summary>
    private void Render(Status s)
    {
        var cfg = Settings.Current;
        var modCount = _g.Mods.ListMods().Count(m => m.IsEnabled);
        var showError = false;

        if (s.Busy)
        {
            Set(OrbState.Working, "Einen Moment …", s.BusyText.Length > 0 ? s.BusyText : "Vextify arbeitet.");
            Buttons(null, Action.None, null, Action.None);
        }
        else if (!s.SpotifyInstalled && s.StoreVersion)
        {
            Set(OrbState.Error, "Das ist die Store-Version von Spotify",
                "Die lässt sich nicht modifizieren. Deinstalliere sie und lade Spotify von der Webseite – Vextify erkennt es danach von selbst.");
            Buttons("Spotify herunterladen", Action.DownloadSpotify, "Store-Version deinstallieren", Action.None, secondaryUri: "ms-settings:appsfeatures");
        }
        else if (!s.SpotifyInstalled)
        {
            Set(OrbState.Error, "Spotify fehlt",
                "Vextify braucht die Desktop-Version von Spotify – nicht die aus dem Microsoft Store.");
            Buttons("Spotify herunterladen", Action.DownloadSpotify, null, Action.None);
        }
        else if (!SpotifyInfo.HasPrefs)
        {
            Set(OrbState.Off, "Öffne Spotify einmal",
                "Melde dich an, warte kurz und schließe es wieder. Danach kann Vextify loslegen.");
            Buttons("Spotify öffnen", Action.OpenSpotify, null, Action.None);
        }
        else if (s.LastError != null)
        {
            Set(OrbState.Error, "Das hat nicht geklappt", s.LastError);
            Buttons("Erneut versuchen", Action.Retry, null, Action.None);
            showError = true;
        }
        else if (s.Patched)
        {
            var n = modCount == 1 ? "1 Mod aktiv" : $"{modCount} Mods aktiv";
            Set(OrbState.Active, "Alles läuft",
                $"{n} · Spotify {s.SpotifyVersion}. Nach Updates erneuert Vextify sie von selbst.");
            Buttons(null, Action.None, "Neu anwenden", Action.Reapply);
        }
        else if (!cfg.Activated)
        {
            Set(OrbState.Off, "Spotify ist noch im Original",
                "Mit einem Klick bekommt es Themes und Extensions. Vextify startet dann mit Windows und kümmert sich um den Rest – auch nach Updates.");
            Buttons("Mods aktivieren", Action.Activate, null, Action.None);
        }
        else
        {
            Set(OrbState.Off, "Deine Mods sind weg",
                cfg.AutoReapply ? "Spotify hat sie überschrieben. Vextify stellt sie gleich wieder her."
                                : "Spotify hat sie überschrieben. Stelle sie mit einem Klick wieder her.");
            Buttons("Wiederherstellen", Action.Activate, null, Action.None);
        }

        SpotifyLine.Text = s.SpotifyInstalled
            ? $"Spotify {s.SpotifyVersion}{(s.SpotifyRunning ? " · läuft gerade" : "")}"
            : "Spotify nicht gefunden";
        EngineLine.Text = s.EngineInstalled ? "Mod-Engine bereit (Spicetify)" : "Mod-Engine wird beim ersten Aktivieren geladen";
        AutoLine.Text = !cfg.Activated ? "Automatische Reparatur startet nach der ersten Aktivierung"
            : cfg.AutoReapply ? "Automatische Reparatur nach Spotify-Updates ist an"
            : "Automatische Reparatur ist aus (Einstellungen)";

        ErrorDetails.Visibility = showError && !string.IsNullOrWhiteSpace(s.LastErrorDetails) ? Visibility.Visible : Visibility.Collapsed;
        ErrorDetails.Text = s.LastErrorDetails ?? "";
        if (showError && ErrorDetails.Visibility == Visibility.Visible) SetDetails(true);
    }

    private void DetailsToggle_Click(object sender, RoutedEventArgs e) =>
        SetDetails(DetailsPanel.Visibility != Visibility.Visible);

    private void SetDetails(bool open)
    {
        DetailsPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        DetailsToggle.Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = open ? Wpf.Ui.Controls.SymbolRegular.ChevronUp24 : Wpf.Ui.Controls.SymbolRegular.ChevronDown24 };
    }

    private void Set(OrbState state, string headline, string sub)
    {
        Orb.State = state;
        Headline.Text = headline;
        Subline.Text = sub;
    }

    private string? _secondaryUri;

    private void Buttons(string? primary, Action primaryAction, string? secondary, Action secondaryAction, string? secondaryUri = null)
    {
        PrimaryButton.Visibility = primary == null ? Visibility.Collapsed : Visibility.Visible;
        PrimaryButton.Content = primary;
        _primary = primaryAction;

        SecondaryButton.Visibility = secondary == null ? Visibility.Collapsed : Visibility.Visible;
        SecondaryButton.Content = secondary;
        _secondary = secondaryAction;
        _secondaryUri = secondaryUri;
    }

    private async void Primary_Click(object sender, RoutedEventArgs e) => await Run(_primary);

    private async void Secondary_Click(object sender, RoutedEventArgs e)
    {
        if (_secondaryUri != null) { OpenUri(_secondaryUri); return; }
        await Run(_secondary);
    }

    private async Task Run(Action action)
    {
        switch (action)
        {
            case Action.Activate:
            case Action.Retry:
            case Action.Reapply:
                Settings.Current.AutoReapply = true;   // an explicit "activate" also turns auto-repair back on after a restore
                Settings.Current.Save();
                var firstTime = !Settings.Current.Activated;
                var ok = await _g.RepairAsync();
                if (ok && firstTime)
                {
                    // The point of Vextify: no console command after a reboot. Starting with Windows is part of "activate"
                    // (stated on the page, reversible in Einstellungen).
                    try { Settings.SetAutostart(true); } catch (Exception ex) { Log.Write("Autostart: " + ex.Message); }
                    App.Instance.MainWin?.ShowToast("Fertig! Vextify startet jetzt mit Windows und hält deine Mods aktiv.");
                }
                else if (ok) App.Instance.MainWin?.ShowToast("Mods angewendet");
                break;
            case Action.DownloadSpotify:
                OpenUri("https://www.spotify.com/download/windows/");
                break;
            case Action.OpenSpotify:
                SpotifyInfo.Start();
                break;
        }
    }

    private static void OpenUri(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
}
