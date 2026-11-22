using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Vextify.Core;
using Wpf.Ui.Controls;
using Border = System.Windows.Controls.Border;
using Image = System.Windows.Controls.Image;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Vextify.Views;

public partial class DiscoverPage : Page
{
    private readonly MarketService _market = new();
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(550) };
    private CancellationTokenSource? _cts;
    private MarketKind _kind = MarketKind.Theme;
    private int _page = 1;
    private bool _first = true;

    public DiscoverPage()
    {
        InitializeComponent();
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = LoadAsync(reset: true); };
        Loaded += (_, _) => { UpdateTabs(); if (_first) { _first = false; _ = LoadAsync(reset: true); } };
        Unloaded += (_, _) => { _debounce.Stop(); _cts?.Cancel(); };
    }

    private void UpdateTabs()
    {
        TabThemes.Appearance = _kind == MarketKind.Theme ? ControlAppearance.Primary : ControlAppearance.Secondary;
        TabExtensions.Appearance = _kind == MarketKind.Extension ? ControlAppearance.Primary : ControlAppearance.Secondary;
    }

    private void TabThemes_Click(object sender, RoutedEventArgs e) => Switch(MarketKind.Theme);
    private void TabExtensions_Click(object sender, RoutedEventArgs e) => Switch(MarketKind.Extension);

    private void Switch(MarketKind kind)
    {
        if (_kind == kind) return;
        _kind = kind;
        UpdateTabs();
        _ = LoadAsync(reset: true);
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _debounce.Stop();
        _debounce.Start();
    }

    private void Retry_Click(object sender, RoutedEventArgs e) => _ = LoadAsync(reset: true);
    private void More_Click(object sender, RoutedEventArgs e) { _page++; _ = LoadAsync(reset: false); }

    private async Task LoadAsync(bool reset)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        if (reset) { _page = 1; Cards.Children.Clear(); }

        MessagePanel.Visibility = Visibility.Collapsed;
        MoreButton.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;

        try
        {
            var query = SearchBox.Text;
            var result = await _market.SearchAsync(_kind, query, _page, cts.Token);
            if (cts.IsCancellationRequested) return;
            LoadingPanel.Visibility = Visibility.Collapsed;

            foreach (var item in result.Items) Cards.Children.Add(BuildCard(item));

            if (Cards.Children.Count == 0)
            {
                var failed = result.Error != null;
                MessageIcon.Symbol = failed ? SymbolRegular.PlugDisconnected24 : SymbolRegular.Search24;
                MessageTitle.Text = failed ? "Keine Verbindung zum Katalog" : "Nichts gefunden";
                MessageText.Text = failed ? result.Error! : $"Zu „{query}“ gibt es keine Treffer. Probier einen anderen Begriff.";
                RetryButton.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
                MessagePanel.Visibility = Visibility.Visible;
            }
            MoreButton.Visibility = result.HasMore && Cards.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) { /* a newer search replaced this one */ }
        catch (Exception ex)
        {
            Log.Write("Entdecken: " + ex);
            LoadingPanel.Visibility = Visibility.Collapsed;
            MessageIcon.Symbol = SymbolRegular.PlugDisconnected24;
            MessageTitle.Text = "Das hat nicht geklappt";
            MessageText.Text = "Der Katalog konnte nicht geladen werden.";
            RetryButton.Visibility = Visibility.Visible;
            MessagePanel.Visibility = Visibility.Visible;
        }
    }

    private FrameworkElement BuildCard(MarketItem item)
    {
        var card = new Border
        {
            Width = 266,
            Margin = new Thickness(0, 0, 12, 12),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("CardStrokeColorDefaultBrush"),
            Background = (Brush)FindResource("CardBackgroundFillColorDefaultBrush"),
            Cursor = Cursors.Hand,
            Focusable = true,
        };
        System.Windows.Automation.AutomationProperties.SetName(card, item.Name);

        // preview: rounded top corners, gradient placeholder underneath while it loads or when there is none
        var imageHost = new Grid { Height = 150 };
        imageHost.Children.Add(new Border { Background = (Brush)FindResource("VxGradient"), Opacity = 0.22 });
        imageHost.Children.Add(new SymbolIcon { Symbol = item.Kind == MarketKind.Theme ? SymbolRegular.PaintBrush24 : SymbolRegular.PuzzlePiece24, FontSize = 34, Opacity = 0.5, Foreground = Brushes.White });
        var image = new Image { Stretch = Stretch.UniformToFill };
        image.Source = ImageHelper.Load(item.PreviewUrl, 540, () => Dispatcher.Invoke(() => image.Source = null));
        imageHost.Children.Add(image);

        var top = new Border { CornerRadius = new CornerRadius(9, 9, 0, 0), ClipToBounds = true, Child = imageHost };

        var info = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };
        info.Children.Add(new TextBlock { Text = item.Name, Style = (Style)FindResource("VxRowTitle"), TextTrimming = TextTrimming.CharacterEllipsis });
        info.Children.Add(new TextBlock { Text = item.Author, Style = (Style)FindResource("VxCaption"), Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        var showDesc = !string.IsNullOrWhiteSpace(item.Description) && !item.Description.Equals(item.Name, StringComparison.OrdinalIgnoreCase);
        info.Children.Add(new TextBlock
        {
            Visibility = showDesc ? Visibility.Visible : Visibility.Collapsed,
            Text = item.Description,
            Style = (Style)FindResource("VxBody"),
            FontSize = 13,
            Margin = new Thickness(0, 8, 0, 0),
            MaxHeight = 38,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var meta = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var stars = new TextBlock { Text = "★ " + Compact(item.Stars), Style = (Style)FindResource("VxCaption") };
        meta.Children.Add(stars);
        if (MarketService.IsInstalled(item))
        {
            var badge = new TextBlock { Text = "Installiert", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("VxAccentTextBrush"), HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(badge, Dock.Right);
            meta.Children.Insert(0, badge);
        }
        info.Children.Add(meta);

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(info);
        card.Child = stack;

        var normal = (Brush)FindResource("CardBackgroundFillColorDefaultBrush");
        var hover = (Brush)FindResource("CardBackgroundFillColorSecondaryBrush");
        card.MouseEnter += (_, _) => card.Background = hover;
        card.MouseLeave += (_, _) => card.Background = normal;
        card.MouseLeftButtonUp += (_, _) => Open(item);
        card.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { Open(item); e.Handled = true; } };
        return card;
    }

    private void Open(MarketItem item)
    {
        var installed = MarketService.IsInstalled(item);
        var sub = $"{item.Author} · ★ {Compact(item.Stars)}" + (item.Tags.Count > 0 ? " · " + string.Join(", ", item.Tags.Take(4)) : "");

        App.Instance.MainWin?.ShowDetail(new DetailModel
        {
            Title = item.Name,
            Subtitle = sub,
            Description = item.Description,
            PreviewUrl = item.PreviewUrl,
            LoadMore = () => _market.GetReadmeAsync(item, CancellationToken.None),
            PrimaryText = installed ? "Erneut installieren" : "Installieren",
            PrimaryAction = async () =>
            {
                var r = await _market.InstallAsync(item);
                if (r.ok) Dispatcher.Invoke(() => RefreshInstalledBadges());
                return r;
            },
            LinkText = "Auf GitHub ansehen",
            LinkUrl = item.RepoUrl,
        });
    }

    /// <summary>Cheapest correct refresh after an install: rebuild the cards from the cached search.</summary>
    private void RefreshInstalledBadges() => _ = LoadAsync(reset: true);

    private static string Compact(int n) => n >= 1000 ? (n / 1000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "k" : n.ToString();
}
