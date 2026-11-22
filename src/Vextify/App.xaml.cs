using System.Drawing;
using System.Windows;
using System.Windows.Media;
using Vextify.Core;
using Vextify.Views;
using Wpf.Ui.Appearance;
using WinForms = System.Windows.Forms;

namespace Vextify;

public partial class App : Application
{
    // System accent for controls (toggles, primary buttons). Windows/WPF-UI derives the exact shade per theme.
    public static readonly System.Windows.Media.Color Accent = System.Windows.Media.Color.FromRgb(0x9B, 0x1F, 0xE8);

    public MainWindow? MainWin => _window;

    private Mutex? _mutex;
    private EventWaitHandle? _showSignal;
    private WinForms.NotifyIcon? _tray;
    private MainWindow? _window;

    public static App Instance => (App)Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance: a second launch just brings the first window forward.
        _mutex = new Mutex(true, @"Local\Vextify.SingleInstance", out var isFirst);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Vextify.Show");
        if (!isFirst)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }
        ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) => Dispatcher.Invoke(ShowWindow), null, Timeout.Infinite, false);

        ApplicationAccentColorManager.Apply(Accent, ApplicationTheme.Dark);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write("UI-Fehler: " + args.Exception);
            args.Handled = true;
        };

        SetupTray();
        Notify_Subscribe();
        Guardian.Instance.Start();

        _window = new MainWindow();
        if (!e.Args.Contains("--tray")) _window.Show();
    }

    private void Notify_Subscribe() =>
        Guardian.Instance.Notify += (title, msg) => Dispatcher.Invoke(() =>
        {
            _tray!.BalloonTipTitle = title;
            _tray.BalloonTipText = msg;
            _tray.ShowBalloonTip(4000);
        });

    private void SetupTray()
    {
        using var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/Vextify.ico"))!.Stream;
        _tray = new WinForms.NotifyIcon
        {
            Icon = new Icon(stream),
            Text = "Vextify",
            Visible = true,
            ContextMenuStrip = new WinForms.ContextMenuStrip(),
        };
        _tray.ContextMenuStrip.Items.Add("Vextify öffnen", null, (_, _) => ShowWindow());
        _tray.ContextMenuStrip.Items.Add("Spotify neu starten", null, async (_, _) => await Guardian.Instance.RestartSpotifyAsync());
        _tray.ContextMenuStrip.Items.Add("Mods jetzt erneuern", null, async (_, _) => await Guardian.Instance.RepairAsync());
        _tray.ContextMenuStrip.Items.Add(new WinForms.ToolStripSeparator());
        _tray.ContextMenuStrip.Items.Add("Beenden", null, (_, _) => Quit());
        _tray.MouseClick += (_, args) => { if (args.Button == WinForms.MouseButtons.Left) ShowWindow(); };
    }

    public void ShowWindow()
    {
        _window ??= new MainWindow();
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Quit()
    {
        Guardian.Instance.Stop();
        _tray?.Dispose();
        _mutex?.ReleaseMutex();
        Shutdown();
    }
}
