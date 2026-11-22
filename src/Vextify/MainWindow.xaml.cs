using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Vextify.Core;
using Vextify.Views;
using Wpf.Ui.Controls;

namespace Vextify;

public partial class MainWindow : FluentWindow
{
    private readonly DispatcherTimer _dropWatch = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private DateTime _lastDragOver;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3.5) };

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            var page = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--page="))?[7..];
            Nav.Navigate(page switch
            {
                "mods" => typeof(ModsPage), "themes" => typeof(ThemesPage), "discover" => typeof(DiscoverPage),
                "settings" => typeof(SettingsPage), _ => typeof(StartPage),
            });
        };

        SmoothScroll.Attach(this);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && DetailOverlay.Visibility == Visibility.Visible) { CloseDetail(); e.Handled = true; } };

        // Keyboard: every top-level place is reachable without the mouse.
        Shortcut(Key.D1, () => Nav.Navigate(typeof(StartPage)));
        Shortcut(Key.D2, () => Nav.Navigate(typeof(ModsPage)));
        Shortcut(Key.D3, () => Nav.Navigate(typeof(ThemesPage)));
        Shortcut(Key.D4, () => Nav.Navigate(typeof(DiscoverPage)));
        Shortcut(Key.OemComma, () => Nav.Navigate(typeof(SettingsPage)));

        // The overlay hides itself once drag events stop arriving (DragLeave fires for every child element, so it can't be trusted).
        _dropWatch.Tick += (_, _) =>
        {
            if (DateTime.UtcNow - _lastDragOver > TimeSpan.FromMilliseconds(250)) HideDropOverlay();
        };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); FadeToast(0); };

        Guardian.Instance.StatusChanged += s => Dispatcher.Invoke(() => UpdatePending(s));
        Loaded += (_, _) => UpdatePending(Guardian.Instance.Current);

        // Tray mode should cost next to nothing: slow polling, and hand memory back to Windows when the window hides.
        IsVisibleChanged += (_, _) =>
        {
            Guardian.Instance.SetWindowVisible(IsVisible);
            if (!IsVisible) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, TrimMemory);
        };
    }

    private void UpdatePending(Status s)
    {
        PendingBar.Visibility = Guardian.Instance.PendingChanges ? Visibility.Visible : Visibility.Collapsed;
        ApplyButton.IsEnabled = !s.Busy;
        ApplyButton.Content = s.Busy ? "Wird angewendet …" : "Anwenden";
        UpdateSidebar(s);
    }

    /// <summary>The status card at the bottom of the sidebar.</summary>
    private void UpdateSidebar(Status s)
    {
        string title, sub;
        System.Windows.Media.Brush dot;
        if (s.Busy) { title = "Arbeitet …"; sub = s.BusyText; dot = (System.Windows.Media.Brush)FindResource("VxAccentTextBrush"); }
        else if (!s.SpotifyInstalled) { title = "Spotify fehlt"; sub = "Siehe Startseite"; dot = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xB4, 0x54)); }
        else if (s.Patched) { title = "Mods aktiv"; sub = $"Spotify {s.SpotifyVersion}"; dot = (System.Windows.Media.Brush)FindResource("VxAccentTextBrush"); }
        else { title = "Nicht aktiv"; sub = $"Spotify {s.SpotifyVersion}"; dot = System.Windows.Media.Brushes.Gray; }

        SideTitle.Text = title;
        SideSub.Text = sub;
        SideDot.Fill = dot;
        SideButton.Content = s.SpotifyRunning ? "Zu Spotify wechseln" : "Spotify öffnen";
        SideButton.IsEnabled = s.SpotifyInstalled;
    }

    private void SideButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Guardian.Instance.Current.SpotifyRunning) { SpotifyInfo.Start(); return; }
        // bring the running Spotify window forward
        var p = System.Diagnostics.Process.GetProcessesByName("Spotify").FirstOrDefault(x => x.MainWindowHandle != IntPtr.Zero);
        if (p != null) SetForegroundWindow(p.MainWindowHandle);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    // ---- detail window -----------------------------------------------------------------------------------------

    private DetailModel? _detail;

    public void ShowDetail(DetailModel m)
    {
        _detail = m;
        DetailTitle.Text = m.Title;
        DetailSub.Text = m.Subtitle ?? "";
        DetailSub.Visibility = string.IsNullOrWhiteSpace(m.Subtitle) ? Visibility.Collapsed : Visibility.Visible;
        DetailDesc.Text = m.Description ?? "";
        DetailPerf.Text = m.PerfNote == null ? "" : "Kann Leistung kosten · " + m.PerfNote;
        DetailPerf.Visibility = m.PerfNote == null ? Visibility.Collapsed : Visibility.Visible;

        DetailImage.Source = null;
        DetailPlaceholder.Visibility = Visibility.Visible;
        var img = ImageHelper.Load(m.PreviewUrl, 1400, () => Dispatcher.Invoke(() => { if (_detail == m) { DetailImage.Source = null; DetailPlaceholder.Visibility = Visibility.Visible; } }));
        if (img != null) { DetailImage.Source = img; DetailPlaceholder.Visibility = Visibility.Collapsed; }

        DetailPrimary.Visibility = m.PrimaryText == null ? Visibility.Collapsed : Visibility.Visible;
        DetailPrimary.Content = m.PrimaryText;
        DetailPrimary.IsEnabled = true;
        DetailLink.Visibility = m.LinkUrl == null ? Visibility.Collapsed : Visibility.Visible;
        DetailLink.Content = m.LinkText ?? "Auf GitHub ansehen";

        DetailOverlay.Opacity = 0;
        DetailOverlay.Visibility = Visibility.Visible;
        if (SystemParameters.ClientAreaAnimation)
            DetailOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        else DetailOverlay.Opacity = 1;

        if (m.LoadMore != null) _ = LoadMoreAsync(m);
    }

    private async Task LoadMoreAsync(DetailModel m)
    {
        try
        {
            var more = await m.LoadMore!();
            if (_detail == m && !string.IsNullOrWhiteSpace(more)) DetailDesc.Text = more;
        }
        catch (Exception ex) { Log.Write("Detail laden: " + ex.Message); }
    }

    private void CloseDetail()
    {
        _detail = null;
        DetailOverlay.BeginAnimation(OpacityProperty, null);
        DetailOverlay.Visibility = Visibility.Collapsed;
        DetailImage.Source = null;
    }

    private void DetailBackdrop_Click(object sender, MouseButtonEventArgs e) => CloseDetail();
    private void DetailCard_Click(object sender, MouseButtonEventArgs e) => e.Handled = true;
    private void DetailClose_Click(object sender, RoutedEventArgs e) => CloseDetail();

    private void DetailLink_Click(object sender, RoutedEventArgs e)
    {
        if (_detail?.LinkUrl != null)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_detail.LinkUrl) { UseShellExecute = true });
    }

    private async void DetailPrimary_Click(object sender, RoutedEventArgs e)
    {
        var m = _detail;
        if (m?.PrimaryAction == null) return;
        var label = DetailPrimary.Content;
        DetailPrimary.IsEnabled = false;
        DetailPrimary.Content = "Bitte warten …";
        try
        {
            var (ok, message) = await m.PrimaryAction();
            ShowToast(message, warning: !ok);
            if (ok && _detail == m) CloseDetail();
        }
        catch (Exception ex)
        {
            Log.Write("Detail-Aktion: " + ex);
            ShowToast("Das hat nicht geklappt: " + ex.Message, warning: true);
        }
        finally
        {
            DetailPrimary.IsEnabled = true;
            DetailPrimary.Content = label;
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var ok = await Guardian.Instance.RepairAsync();
        ShowToast(ok ? "Änderungen angewendet" : "Anwenden hat nicht geklappt – Details auf der Startseite", warning: !ok);
    }

    [System.Runtime.InteropServices.DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr process);

    private static void TrimMemory()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle);
        }
        catch { /* best effort */ }
    }

    private void Shortcut(Key key, System.Action run) =>
        InputBindings.Add(new KeyBinding(new RelayCommand(run), key, ModifierKeys.Control));

    public void NavigateTo(Type page) => Nav.Navigate(page);

    // ---- drag & drop -------------------------------------------------------------------------------------------

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (!hasFiles) return;
        _lastDragOver = DateTime.UtcNow;
        if (DropOverlay.Visibility != Visibility.Visible)
        {
            DropOverlay.Opacity = 0;
            DropOverlay.Visibility = Visibility.Visible;
            if (SystemParameters.ClientAreaAnimation)
                DropOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
            else DropOverlay.Opacity = 1;
            _dropWatch.Start();
        }
    }

    private void Root_Drop(object sender, DragEventArgs e)
    {
        HideDropOverlay();
        ImportHelper.HandleDrop(e);
    }

    private void HideDropOverlay()
    {
        _dropWatch.Stop();
        DropOverlay.BeginAnimation(OpacityProperty, null);
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    // ---- toast -------------------------------------------------------------------------------------------------

    public void ShowToast(string text, bool warning = false)
    {
        ToastText.Text = text;
        ToastIcon.Symbol = warning ? SymbolRegular.Warning24 : SymbolRegular.CheckmarkCircle24;
        ToastIcon.Foreground = warning
            ? (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xB4, 0x54))
            : (System.Windows.Media.Brush)FindResource("VxAccentTextBrush");
        FadeToast(1);
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(warning ? 6 : 3.5);
        _toastTimer.Start();
    }

    private void FadeToast(double to)
    {
        if (!SystemParameters.ClientAreaAnimation) { ToastHost.Opacity = to; return; }
        ToastHost.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(to > 0 ? 180 : 300)));
    }

    // ---- misc --------------------------------------------------------------------------------------------------

    private async void RestartSpotify_Click(object sender, RoutedEventArgs e)
    {
        await Guardian.Instance.RestartSpotifyAsync();
        ShowToast("Spotify wurde neu gestartet");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (Settings.Current.CloseToTray)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            App.Instance.Quit();
        }
        base.OnClosing(e);
    }
}

/// <summary>Tiny ICommand for keyboard shortcuts.</summary>
internal sealed class RelayCommand : ICommand
{
    private readonly System.Action _run;
    public RelayCommand(System.Action run) => _run = run;
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _run();
}
