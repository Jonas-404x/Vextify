using System.Text.Json;
using Microsoft.Win32;

namespace Vextify.Core;

public sealed class Settings
{
    /// <summary>Re-apply the patch automatically when Spotify has overwritten it (e.g. after an update).</summary>
    public bool AutoReapply { get; set; } = true;

    /// <summary>Restart Spotify when a re-apply is needed while it is running.</summary>
    public bool AutoRestartSpotify { get; set; } = true;

    /// <summary>Closing the window keeps Vextify running in the tray.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>
    /// Set after the user's first successful "Mods aktivieren". Until then Vextify never touches Spotify on its own:
    /// a fresh install must get explicit consent before downloading anything or restarting Spotify.
    /// </summary>
    public bool Activated { get; set; }

    /// <summary>SpotifyInfo.Signature() recorded right after our last successful apply.</summary>
    public string PatchedSignature { get; set; } = "";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Vextify";

    public static Settings Current { get; private set; } = Load();

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static Settings Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsFile))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Paths.SettingsFile)) ?? new Settings();
        }
        catch (Exception ex) { Log.Write("Settings laden fehlgeschlagen: " + ex.Message); }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Paths.VextifyData);
            File.WriteAllText(Paths.SettingsFile, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex) { Log.Write("Settings speichern fehlgeschlagen: " + ex.Message); }
    }

    public static bool GetAutostart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is string;
    }

    public static void SetAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Programmpfad unbekannt");
            key.SetValue(RunValue, $"\"{exe}\" --tray");
        }
        else key.DeleteValue(RunValue, throwOnMissingValue: false);
    }
}
