using System.Windows;
using System.Windows.Controls;
using Vextify.Core;
using Wpf.Ui.Controls;

namespace Vextify.Views;

/// <summary>View model row: a ModInfo plus display helpers.</summary>
public sealed class ModRow
{
    public ModInfo Info { get; }
    public ModRow(ModInfo info) => Info = info;
    public string Name => Info.Name;
    public string Description => Info.Description;
    public bool IsEnabled => Info.IsEnabled;
    public string AuthorLabel => string.IsNullOrWhiteSpace(Info.Author) ? "" : "von " + Info.Author;
    public string PerfLabel => Info.PerfNote == null ? "" : "Kann Leistung kosten · " + Info.PerfNote;
}

public partial class ModsPage : Page
{
    private readonly Guardian _g = Guardian.Instance;
    private List<ModInfo> _all = new();

    public ModsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => { _g.StatusChanged += OnStatus; Reload(); };
        Unloaded += (_, _) => _g.StatusChanged -= OnStatus;
    }

    private void OnStatus(Status s) => Dispatcher.Invoke(() =>
    {
        // a drop elsewhere may have added mods, or an apply may have just created the defaults
        if (_all.Count == 0 || _g.PendingChanges && _all.Count != _g.Mods.ListMods().Count) Reload();
    });

    private void Reload()
    {
        _all = _g.Mods.ListMods();
        Render();
    }

    private void Render()
    {
        var q = SearchBox.Text.Trim();
        var shown = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(m => m.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || m.Description.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        var ext = shown.Where(m => m.Kind == ModKind.Extension).Select(m => new ModRow(m)).ToList();
        var apps = shown.Where(m => m.Kind == ModKind.CustomApp).Select(m => new ModRow(m)).ToList();
        ExtList.ItemsSource = ext;
        AppList.ItemsSource = apps;

        ExtHeader.Visibility = ExtList.Visibility = ext.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AppHeader.Visibility = AppList.Visibility = apps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var empty = ext.Count == 0 && apps.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty)
        {
            if (_all.Count == 0)
            {
                EmptyTitle.Text = "Noch keine Mods";
                EmptyText.Text = "Aktiviere Vextify auf der Startseite, dann erscheinen hier die Standard-Mods. Eigene .js-Dateien kannst du jederzeit ins Fenster ziehen.";
                EmptyButton.Visibility = Visibility.Visible;
            }
            else
            {
                EmptyTitle.Text = "Nichts gefunden";
                EmptyText.Text = $"Zu „{q}“ gibt es keinen Mod.";
                EmptyButton.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) { if (IsLoaded) Render(); }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch ts && ts.Tag is ModRow row)
        {
            _g.Mods.SetEnabled(row.Info, ts.IsChecked == true);
            _g.PendingChanges = true;
            _g.Refresh();
        }
    }

    /// <summary>Clicking a row (not its switch) opens the detail window.</summary>
    private void Row_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d != null && !ReferenceEquals(d, sender); d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is ToggleSwitch) return;
        if (sender is not FrameworkElement { Tag: ModRow row }) return;

        var info = row.Info;
        App.Instance.MainWin?.ShowDetail(new DetailModel
        {
            Title = info.Name,
            Subtitle = (info.Kind == ModKind.Extension ? "Extension" : "Custom App") + (string.IsNullOrWhiteSpace(info.Author) ? "" : " · " + info.Author),
            Description = info.Description,
            PreviewUrl = info.PreviewUrl,
            PerfNote = info.PerfNote,
            PrimaryText = info.IsEnabled ? "Ausschalten" : "Einschalten",
            PrimaryAction = () =>
            {
                var enable = !info.IsEnabled;
                _g.Mods.SetEnabled(info, enable);
                _g.PendingChanges = true;
                _g.Refresh();
                Dispatcher.Invoke(Reload);
                return Task.FromResult((true, $"„{info.Name}“ {(enable ? "eingeschaltet" : "ausgeschaltet")} – übernimm es mit „Anwenden“"));
            },
        });
    }

    private void GoDiscover_Click(object sender, RoutedEventArgs e) => App.Instance.MainWin?.NavigateTo(typeof(DiscoverPage));
    private void Add_Click(object sender, RoutedEventArgs e) { ImportHelper.Browse(); Reload(); }
    private void GoStart_Click(object sender, RoutedEventArgs e) => App.Instance.MainWin?.NavigateTo(typeof(StartPage));
}
