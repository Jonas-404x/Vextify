using System.Diagnostics;
using System.IO.Compression;

namespace Vextify.Core;

public static class SpotifyInfo
{
    public static bool IsInstalled => File.Exists(Paths.SpotifyExe);

    /// <summary>The Microsoft Store edition can't be patched; it lives under %LOCALAPPDATA%\Packages.</summary>
    public static bool IsStoreVersionPresent =>
        Directory.Exists(Path.Combine(Paths.LocalAppData, "Packages")) &&
        Directory.GetDirectories(Path.Combine(Paths.LocalAppData, "Packages"), "SpotifyAB.SpotifyMusic_*").Length > 0;

    private static string? _versionCache;
    private static DateTime _versionStamp;

    /// <summary>Cached by the exe's timestamp: the status poll must not re-read the file's version resource every few seconds.</summary>
    public static string? Version
    {
        get
        {
            try
            {
                if (!IsInstalled) return null;
                var stamp = File.GetLastWriteTimeUtc(Paths.SpotifyExe);
                if (_versionCache == null || stamp != _versionStamp)
                {
                    _versionCache = FileVersionInfo.GetVersionInfo(Paths.SpotifyExe).ProductVersion;
                    _versionStamp = stamp;
                }
                return _versionCache;
            }
            catch { return null; }
        }
    }

    public static bool IsRunning => Process.GetProcessesByName("Spotify").Length > 0;

    /// <summary>Spotify has been started at least once (it creates the prefs file on first run).</summary>
    public static bool HasPrefs => File.Exists(Paths.SpotifyPrefs);

    /// <summary>
    /// True when Spotify's UI bundle currently carries Spicetify's runtime. Works for both layouts
    /// Spicetify uses (repacked xpui.spa, or an unpacked Apps\xpui folder).
    /// </summary>
    public static bool IsPatched()
    {
        try
        {
            var unpacked = Path.Combine(Paths.SpotifyApps, "xpui");
            if (Directory.Exists(unpacked))
                return File.Exists(Path.Combine(unpacked, "spicetifyWrapper.js"));

            var spa = Path.Combine(Paths.SpotifyApps, "xpui.spa");
            if (!File.Exists(spa)) return false;
            using var fs = new FileStream(spa, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            return zip.GetEntry("spicetifyWrapper.js") != null;
        }
        catch (Exception ex)
        {
            Log.Write("IsPatched: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Fingerprint of Spotify's UI bundle (size + timestamp of xpui.spa / Apps\xpui) plus the Spotify version.
    /// Recorded right after a successful apply; if it changes, Spotify replaced the files (update).
    /// </summary>
    public static string Signature()
    {
        try
        {
            var spa = new FileInfo(Path.Combine(Paths.SpotifyApps, "xpui.spa"));
            var dir = new DirectoryInfo(Path.Combine(Paths.SpotifyApps, "xpui"));
            return $"{Version}|{(spa.Exists ? spa.Length + ":" + spa.LastWriteTimeUtc.Ticks : "-")}|{(dir.Exists ? dir.LastWriteTimeUtc.Ticks.ToString() : "-")}";
        }
        catch { return ""; }
    }

    public static void Kill()
    {
        foreach (var p in Process.GetProcessesByName("Spotify"))
        {
            try { p.Kill(entireProcessTree: true); p.WaitForExit(5000); } catch { }
        }
    }

    public static void Start()
    {
        if (!IsInstalled) return;
        Process.Start(new ProcessStartInfo(Paths.SpotifyExe) { UseShellExecute = true, WorkingDirectory = Paths.SpotifyDir });
    }
}
