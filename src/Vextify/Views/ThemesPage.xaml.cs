using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Vextify.Core;
using Wpf.Ui.Controls;
using Border = System.Windows.Controls.Border;
using Image = System.Windows.Controls.Image;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Vextify.Views;

public partial class ThemesPage : Page
{
    private readonly Guardian _g = Guardian.Instance;
    private List<ThemeInfo> _themes = new();
    private string _theme = "";
    private string _scheme = "";

    public ThemesPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        _themes = _g.Mods.ListThemes();
        (_theme, _scheme) = _g.Mods.GetCurrentTheme();
        if (_themes.Count > 0 && !_themes.Any(t => t.Name == _theme)) { _theme = _themes[0].Name; _scheme = ""; }
        Rebuild();
    }

    private void Rebuild()
    {
        EmptyState.Visibility = _themes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        Tiles.Children.Clear();
        foreach (var t in _themes) Tiles.Children.Add(BuildTile(t));

        var current = _themes.FirstOrDefault(t => t.Name == _theme);
        Schemes.Children.Clear();
        SchemeSection.Visibility = current is { ColorSchemes.Count: > 1 } ? Visibility.Visible : Visibility.Collapsed;
        if (current != null && !current.ColorSchemes.Contains(_scheme)) _scheme = current.ColorSchemes.FirstOrDefault() ?? "";
        if (current == null) return;
        foreach (var s in current.ColorSchemes)
        {
            var name = s;
            var b = new Wpf.Ui.Controls.Button
            {
                Content = name,
                Margin = new Thickness(0, 0, 8, 8),
                Appearance = name == _scheme ? ControlAppearance.Primary : ControlAppearance.Secondary,
            };
            b.Click += (_, _) => { _scheme = name; Save(); Rebuild(); };
            Schemes.Children.Add(b);
        }
    }

    // ---- tiles -------------------------------------------------------------------------------------------------

    private FrameworkElement BuildTile(ThemeInfo t)
    {
        var selected = t.Name == _theme;
        var pal = (selected && t.Palettes.TryGetValue(_scheme, out var p) ? p : t.Palettes.Values.FirstOrDefault()) ?? new();

        var preview = new Border { Height = 128, CornerRadius = new CornerRadius(8, 8, 0, 0), ClipToBounds = true };
        var shot = ImageHelper.Load(t.PreviewUrl, 420);
        if (shot != null)
            preview.Child = new Image { Source = shot, Stretch = Stretch.UniformToFill };
        else
            preview.Child = MiniSpotify(pal);

        var label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 11, 14, 12) };
        label.Children.Add(new TextBlock { Text = t.Name, Style = (Style)FindResource("VxRowTitle"), VerticalAlignment = VerticalAlignment.Center });
        if (selected)
            label.Children.Add(new SymbolIcon { Symbol = SymbolRegular.CheckmarkCircle24, FontSize = 16, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("VxAccentTextBrush") });

        var stack = new StackPanel();
        stack.Children.Add(preview);
        stack.Children.Add(label);
        if (t.PerfNote != null)
            stack.Children.Add(new TextBlock
            {
                Text = "Aufwendige Effekte: " + t.PerfNote,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0x54)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(14, -6, 14, 12),
            });

        var normal = (Brush)FindResource("CardBackgroundFillColorDefaultBrush");
        var hover = (Brush)FindResource("CardBackgroundFillColorSecondaryBrush");
        var card = new Border
        {
            Width = 210,
            Margin = new Thickness(0, 0, 12, 12),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(selected ? 2 : 1),
            BorderBrush = selected ? (Brush)FindResource("VxAccentTextBrush") : (Brush)FindResource("CardStrokeColorDefaultBrush"),
            Background = normal,
            Child = stack,
            Cursor = Cursors.Hand,
            Focusable = true,
        };
        AutomationProperties.SetName(card, selected ? $"{t.Name}, ausgewählt" : t.Name);
        card.MouseEnter += (_, _) => card.Background = hover;
        card.MouseLeave += (_, _) => card.Background = normal;
        card.MouseLeftButtonUp += (_, _) => Open(t);
        card.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { Open(t); e.Handled = true; } };
        return card;
    }

    /// <summary>Fallback preview painted in the theme's own colors: sidebar, cover grid and player bar of a Spotify window.</summary>
    private static FrameworkElement MiniSpotify(Dictionary<string, string> pal)
    {
        SolidColorBrush Hex(string key, string fallback, double opacity = 1)
        {
            var hex = pal.TryGetValue(key, out var v) ? v : fallback;
            Color c;
            try { c = (Color)ColorConverter.ConvertFromString("#" + hex); } catch { c = (Color)ColorConverter.ConvertFromString("#" + fallback); }
            var b = new SolidColorBrush(c) { Opacity = opacity };
            b.Freeze();
            return b;
        }

        var root = new Grid { Background = Hex("main", "121212") };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.27, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.73, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });

        var side = new Border { Background = Hex("sidebar", "000000") };
        var sideItems = new StackPanel { Margin = new Thickness(9, 11, 9, 0) };
        sideItems.Children.Add(new Ellipse { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Left, Fill = Hex("button", "1db954") });
        foreach (var (w, o) in new[] { (36.0, 0.9), (30.0, 0.5), (34.0, 0.5), (24.0, 0.5) })
            sideItems.Children.Add(new Rectangle { Height = 4, Width = w, RadiusX = 2, RadiusY = 2, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Fill = Hex(o > 0.8 ? "text" : "subtext", "ffffff", o) });
        side.Child = sideItems;

        var content = new StackPanel { Margin = new Thickness(11, 11, 11, 0) };
        content.Children.Add(new Rectangle { Height = 6, Width = 64, RadiusX = 3, RadiusY = 3, HorizontalAlignment = HorizontalAlignment.Left, Fill = Hex("text", "ffffff") });
        var covers = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
        foreach (var o in new[] { 0.95, 0.65, 0.4 })
            covers.Children.Add(new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 7, 0), Background = Hex("button", "1db954", o) });
        content.Children.Add(covers);
        content.Children.Add(new Rectangle { Height = 4, Width = 90, RadiusX = 2, RadiusY = 2, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Fill = Hex("subtext", "b3b3b3", 0.6) });
        Grid.SetColumn(content, 1);

        var player = new Border { Background = Hex("player", "181818") };
        var bar = new Grid { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(new Rectangle { Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = Hex("subtext", "b3b3b3", 0.35), Margin = new Thickness(26, 0, 0, 0) });
        bar.Children.Add(new Rectangle { Height = 3, RadiusX = 1.5, RadiusY = 1.5, Width = 54, HorizontalAlignment = HorizontalAlignment.Left, Fill = Hex("button", "1db954"), Margin = new Thickness(26, 0, 0, 0) });
        bar.Children.Add(new Ellipse { Width = 10, Height = 10, HorizontalAlignment = HorizontalAlignment.Left, Fill = Hex("text", "ffffff") });
        player.Child = bar;
        Grid.SetRow(player, 1);
        Grid.SetColumnSpan(player, 2);

        root.Children.Add(side);
        root.Children.Add(content);
        root.Children.Add(player);
        return root;
    }

    // ---- actions -----------------------------------------------------------------------------------------------

    private void Open(ThemeInfo t)
    {
        var isCurrent = t.Name == _theme;
        var schemes = t.ColorSchemes.Count > 1 ? "Farbschemata: " + string.Join(", ", t.ColorSchemes) : null;
        var desc = t.Description ?? schemes ?? "Ein Theme für Spotify.";
        if (t.Description != null && schemes != null) desc += "\n\n" + schemes;

        App.Instance.MainWin?.ShowDetail(new DetailModel
        {
            Title = t.Name,
            Subtitle = string.IsNullOrWhiteSpace(t.Author) ? "Theme" : "Theme · " + t.Author,
            Description = desc,
            PreviewUrl = t.PreviewUrl,
            PerfNote = t.PerfNote,
            PrimaryText = isCurrent ? null : "Verwenden",
            PrimaryAction = () =>
            {
                _theme = t.Name;
                _scheme = "";
                Save();
                Dispatcher.Invoke(Rebuild);
                return Task.FromResult((true, $"„{t.Name}“ ausgewählt – übernimm es mit „Anwenden“"));
            },
        });
    }

    private void Save()
    {
        if (string.IsNullOrEmpty(_theme)) return;
        _g.Mods.SetTheme(_theme, _scheme);
        _g.PendingChanges = true;
        _g.Refresh();
    }

    private void GoDiscover_Click(object sender, RoutedEventArgs e) => App.Instance.MainWin?.NavigateTo(typeof(DiscoverPage));
}
