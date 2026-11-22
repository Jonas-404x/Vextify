using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Vextify.Core;

public enum ModKind { Extension, CustomApp }

public sealed class ModInfo
{
    public required string Id { get; init; }          // file name for extensions (a.js), folder name for apps
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string? Author { get; init; }
    public required ModKind Kind { get; init; }
    public bool IsEnabled { get; set; }

    /// <summary>Why this mod may cost CPU/GPU (from scanning its code), or null.</summary>
    public string? PerfNote { get; init; }

    /// <summary>Preview image, only known for mods installed from Entdecken.</summary>
    public string? PreviewUrl { get; init; }
}

public sealed class ThemeInfo
{
    public required string Name { get; init; }
    public List<string> ColorSchemes { get; init; } = new();

    /// <summary>Why this theme may cost GPU (from scanning user.css), or null.</summary>
    public string? PerfNote { get; init; }

    /// <summary>Screenshot and text, only known for themes installed from Entdecken.</summary>
    public string? PreviewUrl { get; init; }
    public string? Description { get; init; }
    public string? Author { get; init; }

    /// <summary>Scheme name → colors (hex without #) for the preview tile: main, sidebar, player, button, text.</summary>
    public Dictionary<string, Dictionary<string, string>> Palettes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Reads and edits Spicetify's mods through its config file. Applying is a separate step.</summary>
public sealed class ModService
{
    private const string Section = "AdditionalOptions";

    // Human descriptions for the extensions that ship with Spicetify (German).
    private static readonly Dictionary<string, string> KnownDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["autoSkipExplicit.js"] = "Überspringt automatisch Songs, die als explizit markiert sind.",
        ["autoSkipVideo.js"] = "Überspringt Videos, die in deiner Region nicht abspielbar sind.",
        ["bookmark.js"] = "Merkt dir Songs, Alben und Playlists als Lesezeichen und springt später zurück.",
        ["fullAppDisplay.js"] = "Vollbild-Ansicht mit großem Cover, Songtitel und Fortschritt.",
        ["keyboardShortcut.js"] = "Vim-ähnliche Tastenkürzel zum Navigieren und Steuern von Spotify.",
        ["loopyLoop.js"] = "Wiederholt einen frei gewählten Abschnitt eines Songs in Dauerschleife.",
        ["popupLyrics.js"] = "Zeigt Songtexte in einem eigenen, verschiebbaren Fenster.",
        ["shuffle+.js"] = "Echtes, zufälliges Shuffle statt Spotifys Algorithmus.",
        ["trashbin.js"] = "Mit einem Klick Songs und Künstler aus der Wiedergabe verbannen.",
        ["webnowplaying.js"] = "Schickt den aktuellen Song an Programme wie Rainmeter.",
        ["adblock.js"] = "Blockt Werbung (Spicetify-Variante).",
        ["hidePodcasts.js"] = "Blendet Podcasts in der Oberfläche aus.",
    };

    public IEnumerable<string> ExtensionDirs => new[] { Path.Combine(Paths.SpicetifyConfigDir, "Extensions"), Path.Combine(Paths.SpicetifyExeDir, "Extensions") };
    public IEnumerable<string> AppDirs => new[] { Path.Combine(Paths.SpicetifyConfigDir, "CustomApps"), Path.Combine(Paths.SpicetifyExeDir, "CustomApps") };
    public IEnumerable<string> ThemeDirs => new[] { Path.Combine(Paths.SpicetifyConfigDir, "Themes"), Path.Combine(Paths.SpicetifyExeDir, "Themes") };

    private static HashSet<string> EnabledSet(IniFile ini, string key) =>
        (ini.Get(Section, key) ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public List<ModInfo> ListMods()
    {
        var ini = new IniFile(Paths.SpicetifyConfigIni);
        var ext = EnabledSet(ini, "extensions");
        var apps = EnabledSet(ini, "custom_apps");
        var result = new List<ModInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in ExtensionDirs.Where(Directory.Exists))
            foreach (var file in Directory.GetFiles(dir, "*.js"))
            {
                var id = Path.GetFileName(file);
                if (!seen.Add("e:" + id)) continue;
                var (name, author, desc) = ReadHeader(file);
                var meta = MetaStore.Get(MetaStore.ExtKey(id));
                result.Add(new ModInfo
                {
                    Id = id,
                    Name = name ?? Path.GetFileNameWithoutExtension(id),
                    Author = author ?? meta?.Author,
                    PreviewUrl = meta?.PreviewUrl,
                    Description = KnownDescriptions.TryGetValue(id, out var d) ? d : meta?.Description ?? desc ?? "Keine Beschreibung vorhanden.",
                    Kind = ModKind.Extension,
                    IsEnabled = ext.Contains(id),
                    PerfNote = ScanPerf(file, css: false),
                });
            }

        foreach (var dir in AppDirs.Where(Directory.Exists))
            foreach (var folder in Directory.GetDirectories(dir))
            {
                var id = Path.GetFileName(folder);
                if (!seen.Add("a:" + id)) continue;
                result.Add(new ModInfo
                {
                    Id = id,
                    Name = id,
                    Description = id.Equals("reddit", StringComparison.OrdinalIgnoreCase) ? "Reddit-Feed direkt in Spotify."
                        : id.Equals("lyrics-plus", StringComparison.OrdinalIgnoreCase) ? "Synchronisierte Songtexte in einer eigenen Seite."
                        : id.Equals("new-releases", StringComparison.OrdinalIgnoreCase) ? "Neuerscheinungen deiner Künstler auf einen Blick."
                        : "Eigene Seite in der Spotify-Seitenleiste.",
                    Kind = ModKind.CustomApp,
                    IsEnabled = apps.Contains(id),
                });
            }

        return result.OrderBy(m => m.Kind).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void SetEnabled(ModInfo mod, bool enabled)
    {
        var ini = new IniFile(Paths.SpicetifyConfigIni);
        var key = mod.Kind == ModKind.Extension ? "extensions" : "custom_apps";
        var set = EnabledSet(ini, key);
        if (enabled) set.Add(mod.Id); else set.Remove(mod.Id);
        ini.Set(Section, key, string.Join('|', set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)));
        ini.Save();
        mod.IsEnabled = enabled;
    }

    /// <summary>Turn on an extension by file name (used right after installing it from Entdecken).</summary>
    public void EnableExtension(string file)
    {
        var ini = new IniFile(Paths.SpicetifyConfigIni);
        var set = EnabledSet(ini, "extensions");
        set.Add(file);
        ini.Set(Section, "extensions", string.Join('|', set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)));
        ini.Save();
    }

    public List<ThemeInfo> ListThemes()
    {
        var list = new List<ThemeInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in ThemeDirs.Where(Directory.Exists))
            foreach (var folder in Directory.GetDirectories(dir))
            {
                var name = Path.GetFileName(folder);
                if (!seen.Add(name)) continue;
                var colorIni = Path.Combine(folder, "color.ini");
                var schemes = new List<string>();
                var palettes = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(colorIni))
                {
                    string? section = null;
                    foreach (var raw in File.ReadLines(colorIni))
                    {
                        var line = raw.Trim();
                        if (line.StartsWith('[') && line.EndsWith(']'))
                        {
                            section = line[1..^1];
                            schemes.Add(section);
                            palettes[section] = new(StringComparer.OrdinalIgnoreCase);
                        }
                        else if (section != null && line.Contains('=') && !line.StartsWith(';') && !line.StartsWith('#'))
                        {
                            var eq = line.IndexOf('=');
                            palettes[section][line[..eq].Trim()] = line[(eq + 1)..].Trim().TrimStart('#');
                        }
                    }
                }
                var tmeta = MetaStore.Get(MetaStore.ThemeKey(name));
                list.Add(new ThemeInfo
                {
                    Name = name, ColorSchemes = schemes, Palettes = palettes,
                    PerfNote = ScanPerf(Path.Combine(folder, "user.css"), css: true),
                    PreviewUrl = tmeta?.PreviewUrl, Description = tmeta?.Description, Author = tmeta?.Author,
                });
            }
        return list.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Spicetify keeps the active theme under [Setting] (extensions and custom apps live under [AdditionalOptions]).
    private const string ThemeSection = "Setting";

    public (string theme, string scheme) GetCurrentTheme()
    {
        var ini = new IniFile(Paths.SpicetifyConfigIni);
        return (ini.Get(ThemeSection, "current_theme") ?? "", ini.Get(ThemeSection, "color_scheme") ?? "");
    }

    public void SetTheme(string theme, string scheme)
    {
        var ini = new IniFile(Paths.SpicetifyConfigIni);
        ini.Set(ThemeSection, "current_theme", theme);
        ini.Set(ThemeSection, "color_scheme", scheme);
        ini.Save();
    }

    /// <summary>Adds dropped files: .js → extension, .css → minimal theme, .zip → theme/custom app, folder → theme/custom app.</summary>
    public (int added, List<string> problems) Import(IEnumerable<string> paths)
    {
        var added = 0;
        var problems = new List<string>();
        var extDir = Path.Combine(Paths.SpicetifyConfigDir, "Extensions");
        var themeDir = Path.Combine(Paths.SpicetifyConfigDir, "Themes");
        var appDir = Path.Combine(Paths.SpicetifyConfigDir, "CustomApps");

        foreach (var path in paths)
        {
            try
            {
                var name = Path.GetFileName(path);
                if (Directory.Exists(path)) { added += ImportFolder(path, themeDir, appDir, problems); continue; }
                switch (Path.GetExtension(path).ToLowerInvariant())
                {
                    case ".js":
                        Directory.CreateDirectory(extDir);
                        File.Copy(path, Path.Combine(extDir, name), overwrite: true);
                        added++;
                        break;
                    case ".css":
                        var themeName = Path.GetFileNameWithoutExtension(path);
                        var target = Path.Combine(themeDir, themeName);
                        Directory.CreateDirectory(target);
                        File.Copy(path, Path.Combine(target, "user.css"), overwrite: true);
                        if (!File.Exists(Path.Combine(target, "color.ini"))) File.WriteAllText(Path.Combine(target, "color.ini"), DefaultColorIni);
                        added++;
                        break;
                    case ".zip":
                        var tmp = Path.Combine(Path.GetTempPath(), "vextify-import-" + Guid.NewGuid().ToString("N"));
                        ZipFile.ExtractToDirectory(path, tmp);
                        var root = Directory.GetFiles(tmp, "user.css", SearchOption.AllDirectories).Select(Path.GetDirectoryName).FirstOrDefault()
                                   ?? Directory.GetFiles(tmp, "manifest.json", SearchOption.AllDirectories).Select(Path.GetDirectoryName).FirstOrDefault();
                        if (root == null) { problems.Add($"{name}: weder Theme noch Custom App erkannt."); Directory.Delete(tmp, true); break; }
                        added += ImportFolder(root!, themeDir, appDir, problems, Path.GetFileNameWithoutExtension(path));
                        Directory.Delete(tmp, true);
                        break;
                    default:
                        problems.Add($"{name}: Dateityp wird nicht unterstützt (nur .js, .css, .zip).");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Write("Import: " + ex);
                problems.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }
        return (added, problems);
    }

    private static int ImportFolder(string folder, string themeDir, string appDir, List<string> problems, string? nameOverride = null)
    {
        var name = nameOverride ?? Path.GetFileName(folder.TrimEnd('\\', '/'));
        if (File.Exists(Path.Combine(folder, "user.css")))
        {
            CopyDir(folder, Path.Combine(themeDir, name));
            return 1;
        }
        if (File.Exists(Path.Combine(folder, "manifest.json")))
        {
            CopyDir(folder, Path.Combine(appDir, name));
            return 1;
        }
        problems.Add($"{name}: Ordner enthält weder user.css (Theme) noch manifest.json (Custom App).");
        return 0;
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    /// <summary>
    /// Cheap static check for the patterns that usually make mods expensive in Spotify's embedded browser:
    /// blur/backdrop filters and endless animations (GPU), render loops and short timers (CPU).
    /// It's a hint, not a measurement.
    /// </summary>
    private static string? ScanPerf(string file, bool css)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > 3_000_000) return null;
            var text = File.ReadAllText(file);
            var reasons = new List<string>();
            if (css)
            {
                if (Regex.IsMatch(text, @"backdrop-filter|filter\s*:[^;{}]*blur\(", RegexOptions.IgnoreCase)) reasons.Add("Weichzeichner (GPU)");
                if (Regex.IsMatch(text, @"animation[^;{}]*infinite", RegexOptions.IgnoreCase)) reasons.Add("Dauer-Animationen");
            }
            else
            {
                if (text.Contains("requestAnimationFrame")) reasons.Add("Dauer-Rendering");
                if (Regex.IsMatch(text, @"setInterval\s*\(")) reasons.Add("Hintergrund-Timer");
            }
            return reasons.Count == 0 ? null : string.Join(", ", reasons);
        }
        catch { return null; }
    }

    private static (string? name, string? author, string? desc) ReadHeader(string file)
    {
        try
        {
            string? name = null, author = null, desc = null;
            foreach (var line in File.ReadLines(file).Take(15))
            {
                var m = Regex.Match(line, @"^\s*//\s*(NAME|AUTHOR|DESCRIPTION)\s*:\s*(.+?)\s*$", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                switch (m.Groups[1].Value.ToUpperInvariant())
                {
                    case "NAME": name = m.Groups[2].Value; break;
                    case "AUTHOR": author = m.Groups[2].Value; break;
                    case "DESCRIPTION": desc = m.Groups[2].Value; break;
                }
            }
            return (name, author, desc);
        }
        catch { return (null, null, null); }
    }

    private const string DefaultColorIni = """
        [Base]
        text               = ffffff
        subtext            = b3b3b3
        main               = 121212
        sidebar            = 000000
        player             = 181818
        card               = 282828
        shadow             = 000000
        selected-row       = ffffff
        button             = 1db954
        button-active      = 1ed760
        button-disabled    = 535353
        tab-active         = 333333
        notification       = 2e77d0
        notification-error = e22134
        equalizer          = 1db954
        misc               = 535353
        """;
}
