namespace Vextify.Core;

/// <summary>All well-known locations Vextify touches.</summary>
public static class Paths
{
    public static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    // Spotify (classic desktop install)
    public static readonly string SpotifyDir = Path.Combine(AppData, "Spotify");
    public static readonly string SpotifyExe = Path.Combine(SpotifyDir, "Spotify.exe");
    public static readonly string SpotifyApps = Path.Combine(SpotifyDir, "Apps");
    public static readonly string SpotifyPrefs = Path.Combine(SpotifyDir, "prefs");

    // Spicetify: config/user data in %APPDATA%, program files in %LOCALAPPDATA% (same layout as the official installer)
    public static readonly string SpicetifyConfigDir = Path.Combine(AppData, "spicetify");
    public static readonly string SpicetifyConfigIni = Path.Combine(SpicetifyConfigDir, "config-xpui.ini");
    public static readonly string SpicetifyExeDir = Path.Combine(LocalAppData, "spicetify");
    public static readonly string SpicetifyExe = Path.Combine(SpicetifyExeDir, "spicetify.exe");

    // Vextify's own data
    public static readonly string VextifyData = Path.Combine(AppData, "Vextify");
    public static readonly string SettingsFile = Path.Combine(VextifyData, "settings.json");
    public static readonly string LogFile = Path.Combine(VextifyData, "vextify.log");
}
