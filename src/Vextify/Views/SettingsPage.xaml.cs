using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Vextify.Core;

namespace Vextify.Views;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        // Read on every Loaded, not once: the page may be cached, and activation can switch autostart on in the meantime.
        Loaded += (_, _) =>
        {
            var s = Settings.Current;
            AutostartSwitch.IsChecked = Settings.GetAutostart();
            ReapplySwitch.IsChecked = s.AutoReapply;
            RestartSwitch.IsChecked = s.AutoRestartSpotify;
            TraySwitch.IsChecked = s.CloseToTray;
            VersionText.Text = $"Vextify {typeof(SettingsPage).Assembly.GetName().Version?.ToString(3)}";
        };
    }

    private void Autostart_Click(object sender, RoutedEventArgs e)
    {
        try { Settings.SetAutostart(AutostartSwitch.IsChecked == true); }
        catch (Exception ex)
        {
            Log.Write("Autostart: " + ex);
            AutostartSwitch.IsChecked = Settings.GetAutostart();
            System.Windows.MessageBox.Show("Autostart konnte nicht geändert werden: " + ex.Message, "Vextify");
        }
    }

    private void Reapply_Click(object sender, RoutedEventArgs e) { Settings.Current.AutoReapply = ReapplySwitch.IsChecked == true; Settings.Current.Save(); }
    private void Restart_Click(object sender, RoutedEventArgs e) { Settings.Current.AutoRestartSpotify = RestartSwitch.IsChecked == true; Settings.Current.Save(); }
    private void Tray_Click(object sender, RoutedEventArgs e) { Settings.Current.CloseToTray = TraySwitch.IsChecked == true; Settings.Current.Save(); }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var ok = System.Windows.MessageBox.Show(
            "Spotify wird auf den Originalzustand zurückgesetzt, alle Mods sind dann weg. Die automatische Reparatur wird pausiert, bis du auf der Startseite wieder aktivierst. Fortfahren?",
            "Vextify", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;
        ReapplySwitch.IsChecked = false;
        await Guardian.Instance.RestoreAsync();
    }

    private void Log_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Paths.VextifyData);
        Process.Start(new ProcessStartInfo(Paths.VextifyData) { UseShellExecute = true });
    }
}
