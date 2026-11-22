using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vextify.Core;

public enum MarketKind { Theme, Extension }

public sealed class MarketItem
{
    public MarketKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public int Stars { get; set; }
    public string Repo { get; set; } = "";
    public string Branch { get; set; } = "main";
    public string? PreviewUrl { get; set; }
    public string? MainPath { get; set; }       // extensions: the .js file
    public string? UserCssPath { get; set; }    // themes: user.css
    public string? SchemesPath { get; set; }    // themes: color.ini
    public string? ReadmePath { get; set; }
    public List<string> Tags { get; set; } = new();

    public string RepoUrl => "https://github.com/" + Repo;
    public string ExtFileName => Path.GetFileName(MainPath ?? Name + ".js");
    public string ThemeFolderName => MarketService.SafeName(Name);
}

public sealed record MarketResult(List<MarketItem> Items, string? Error, bool HasMore);

/// <summary>
/// The Spicetify community catalog. Repos publish themselves with a GitHub topic (spicetify-themes / spicetify-extensions)
/// and a manifest.json in the repo root, which is how the official Marketplace discovers them too.
/// </summary>
public sealed class MarketService
{
    private const int PageSize = 24;
    private static readonly string CacheDir = Path.Combine(Paths.VextifyData, "cache");
    private static readonly Dictionary<string, string?> ReadmeCache = new();

    // ---- search ------------------------------------------------------------------------------------------------

    public async Task<MarketResult> SearchAsync(MarketKind kind, string? query, int page, CancellationToken ct)
    {
        var topic = kind == MarketKind.Theme ? "spicetify-themes" : "spicetify-extensions";
        var q = (string.IsNullOrWhiteSpace(query) ? "" : Uri.EscapeDataString(query.Trim()) + "+") + $"topic:{topic}+archived:false";
        var url = $"https://api.github.com/search/repositories?q={q}&sort=stars&order=desc&per_page={PageSize}&page={page}";

        var cacheFile = Path.Combine(CacheDir, $"{kind}-{Hash(query ?? "")}-{page}.json");
        var fresh = ReadCache(cacheFile, TimeSpan.FromHours(6));
        if (fresh != null) return new MarketResult(fresh, null, fresh.Count >= 8);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await Net.Http.SendAsync(req, ct);
            if (resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                return Fallback(cacheFile, "GitHub lässt gerade keine weiteren Suchen zu. Warte eine Minute und versuche es erneut.");
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var repos = doc.RootElement.GetProperty("items").EnumerateArray().Select(ParseRepo).ToList();

            var gate = new SemaphoreSlim(8);
            var lists = await Task.WhenAll(repos.Select(async r =>
            {
                await gate.WaitAsync(ct);
                try { return await LoadManifestAsync(kind, r, ct); }
                finally { gate.Release(); }
            }));

            var items = lists.SelectMany(l => l).ToList();
            WriteCache(cacheFile, items);
            return new MarketResult(items, null, repos.Count >= PageSize);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Write("Marketplace-Suche: " + ex.Message);
            return Fallback(cacheFile, "Der Katalog konnte nicht geladen werden. Prüfe deine Internetverbindung.");
        }
    }

    private static MarketResult Fallback(string cacheFile, string error)
    {
        var stale = ReadCache(cacheFile, TimeSpan.MaxValue);
        return stale != null ? new MarketResult(stale, null, false) : new MarketResult(new(), error, false);
    }

    private sealed record Repo(string FullName, string Name, string Description, string Owner, int Stars, string Branch);

    private static Repo ParseRepo(JsonElement e) => new(
        e.GetProperty("full_name").GetString() ?? "",
        e.GetProperty("name").GetString() ?? "",
        e.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : "",
        e.GetProperty("owner").GetProperty("login").GetString() ?? "",
        e.GetProperty("stargazers_count").GetInt32(),
        e.TryGetProperty("default_branch", out var b) ? b.GetString() ?? "main" : "main");

    private static async Task<List<MarketItem>> LoadManifestAsync(MarketKind kind, Repo r, CancellationToken ct)
    {
        var result = new List<MarketItem>();
        try
        {
            var text = await Net.Http.GetStringAsync(Raw(r.FullName, r.Branch, "manifest.json"), ct);
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var entries = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };

            // collection repos (e.g. the official themes repo) list many entries in one manifest: show them all
            foreach (var m in entries.Take(60))
            {
                if (m.ValueKind != JsonValueKind.Object) continue;
                var name = Str(m, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;

                var item = new MarketItem
                {
                    Kind = kind,
                    Name = name!,
                    Description = Str(m, "description") ?? r.Description,
                    Repo = r.FullName,
                    Branch = Str(m, "branch") ?? r.Branch,
                    Stars = r.Stars,
                    Author = FirstAuthor(m) ?? r.Owner,
                    MainPath = Str(m, "main"),
                    UserCssPath = Str(m, "usercss"),
                    SchemesPath = Str(m, "schemes"),
                    ReadmePath = Str(m, "readme"),
                    Tags = m.TryGetProperty("tags", out var tg) && tg.ValueKind == JsonValueKind.Array
                        ? tg.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).Take(6).ToList() : new(),
                };
                item.PreviewUrl = Resolve(item, Str(m, "preview"));

                if (kind == MarketKind.Extension && string.IsNullOrWhiteSpace(item.MainPath)) continue;
                if (kind == MarketKind.Theme && string.IsNullOrWhiteSpace(item.UserCssPath)) continue;
                result.Add(item);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* repo without a usable manifest: not listed */ }
        return result;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? FirstAuthor(JsonElement m)
    {
        if (!m.TryGetProperty("authors", out var a) || a.ValueKind != JsonValueKind.Array) return null;
        foreach (var x in a.EnumerateArray())
            if (x.ValueKind == JsonValueKind.Object && Str(x, "name") is { Length: > 0 } n) return n;
        return null;
    }

    // ---- readme ------------------------------------------------------------------------------------------------

    public async Task<string?> GetReadmeAsync(MarketItem item, CancellationToken ct)
    {
        var key = item.Repo + "|" + item.ReadmePath;
        if (ReadmeCache.TryGetValue(key, out var cached)) return cached;
        string? result = null;
        try
        {
            var text = await Net.Http.GetStringAsync(Resolve(item, item.ReadmePath ?? "README.md"), ct);
            result = CleanMarkdown(text);
        }
        catch (OperationCanceledException) { throw; }
        catch { /* no readme: the manifest description stays */ }
        ReadmeCache[key] = result;
        return result;
    }

    private static string CleanMarkdown(string md)
    {
        var s = Regex.Replace(md, @"<!--.*?-->", "", RegexOptions.Singleline);
        s = Regex.Replace(s, @"!\[[^\]]*\]\([^)]*\)", "");
        s = Regex.Replace(s, @"<[^>]+>", "");
        s = Regex.Replace(s, @"\[([^\]]*)\]\([^)]*\)", "$1");
        s = Regex.Replace(s, @"^\s{0,3}#{1,6}\s*", "", RegexOptions.Multiline);
        s = s.Replace("**", "").Replace("__", "").Replace("`", "");
        s = Regex.Replace(s, @"[ \t]+\r?\n", "\n");
        s = Regex.Replace(s, @"(\r?\n){3,}", "\n\n").Trim();
        return s.Length > 1800 ? s[..1800].TrimEnd() + " …" : s;
    }

    // ---- install -----------------------------------------------------------------------------------------------

    public async Task<(bool ok, string message)> InstallAsync(MarketItem item, IProgress<string>? progress = null)
    {
        try
        {
            return item.Kind == MarketKind.Extension ? await InstallExtensionAsync(item, progress) : await InstallThemeAsync(item, progress);
        }
        catch (Exception ex)
        {
            Log.Write($"Installation {item.Name}: {ex}");
            return (false, "Installation fehlgeschlagen: " + ex.Message);
        }
    }

    private static async Task<(bool, string)> InstallExtensionAsync(MarketItem item, IProgress<string>? progress)
    {
        progress?.Report("Lade Extension …");
        var bytes = await Net.Http.GetByteArrayAsync(Resolve(item, item.MainPath));
        var file = item.ExtFileName;
        if (!file.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) file += ".js";

        var dir = Path.Combine(Paths.SpicetifyConfigDir, "Extensions");
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, file), bytes);

        MetaStore.Set(MetaStore.ExtKey(file), new ItemMeta
        {
            Description = item.Description, PreviewUrl = item.PreviewUrl, Author = item.Author,
            Repo = item.Repo, ReadmePath = item.ReadmePath, Branch = item.Branch,
        });
        Guardian.Instance.Mods.EnableExtension(file);
        Guardian.Instance.PendingChanges = true;
        Guardian.Instance.Refresh();
        return (true, $"„{item.Name}“ installiert und eingeschaltet – übernimm es mit „Anwenden“");
    }

    private static async Task<(bool, string)> InstallThemeAsync(MarketItem item, IProgress<string>? progress)
    {
        progress?.Report("Lade Theme …");
        var tmpRoot = Path.Combine(Path.GetTempPath(), "vextify-theme-" + Guid.NewGuid().ToString("N"));
        var zipPath = tmpRoot + ".zip";
        try
        {
            await using (var s = await Net.Http.GetStreamAsync($"https://github.com/{item.Repo}/archive/refs/heads/{item.Branch}.zip"))
            await using (var f = File.Create(zipPath))
                await s.CopyToAsync(f);

            progress?.Report("Entpacke …");
            ZipFile.ExtractToDirectory(zipPath, tmpRoot);
            var repoRoot = Directory.GetDirectories(tmpRoot).FirstOrDefault() ?? tmpRoot;

            // find the folder that holds user.css, as the manifest says
            var cssRel = (item.UserCssPath ?? "user.css").Replace('/', Path.DirectorySeparatorChar).TrimStart('.', Path.DirectorySeparatorChar);
            var src = Path.GetDirectoryName(Path.Combine(repoRoot, cssRel)) ?? repoRoot;
            if (!File.Exists(Path.Combine(src, Path.GetFileName(cssRel))))
                src = Directory.GetFiles(repoRoot, "user.css", SearchOption.AllDirectories).Select(Path.GetDirectoryName).FirstOrDefault()
                      ?? throw new InvalidOperationException("Im Theme wurde keine user.css gefunden.");

            var name = item.ThemeFolderName;
            var dest = Path.Combine(Paths.SpicetifyConfigDir, "Themes", name);
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            CopyDir(src, dest);

            if (!File.Exists(Path.Combine(dest, "color.ini")) && item.SchemesPath != null)
            {
                var schemes = Path.Combine(repoRoot, item.SchemesPath.Replace('/', Path.DirectorySeparatorChar).TrimStart('.', Path.DirectorySeparatorChar));
                if (File.Exists(schemes)) File.Copy(schemes, Path.Combine(dest, "color.ini"), true);
            }
            if (!File.Exists(Path.Combine(dest, "color.ini")))
                File.WriteAllText(Path.Combine(dest, "color.ini"), "[Base]\ntext = ffffff\nmain = 121212\nsidebar = 000000\nplayer = 181818\nbutton = 1db954\n");

            MetaStore.Set(MetaStore.ThemeKey(name), new ItemMeta
            {
                Description = item.Description, PreviewUrl = item.PreviewUrl, Author = item.Author,
                Repo = item.Repo, ReadmePath = item.ReadmePath, Branch = item.Branch,
            });

            var mods = Guardian.Instance.Mods;
            var first = mods.ListThemes().FirstOrDefault(t => t.Name == name)?.ColorSchemes.FirstOrDefault() ?? "";
            mods.SetTheme(name, first);
            Guardian.Instance.PendingChanges = true;
            Guardian.Instance.Refresh();
            return (true, $"„{item.Name}“ installiert und ausgewählt – übernimm es mit „Anwenden“");
        }
        finally
        {
            try { if (Directory.Exists(tmpRoot)) Directory.Delete(tmpRoot, true); } catch { }
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
        }
    }

    /// <summary>Looks in both places Spicetify reads from: the user's config folder and its own program folder (where the built-ins live).</summary>
    public static bool IsInstalled(MarketItem item) => item.Kind == MarketKind.Theme
        ? new[] { Paths.SpicetifyConfigDir, Paths.SpicetifyExeDir }.Any(d => Directory.Exists(Path.Combine(d, "Themes", item.ThemeFolderName)))
        : new[] { Paths.SpicetifyConfigDir, Paths.SpicetifyExeDir }.Any(d => File.Exists(Path.Combine(d, "Extensions", item.ExtFileName)));

    // ---- helpers -----------------------------------------------------------------------------------------------

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    public static string SafeName(string name)
    {
        var s = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
        return s.Length == 0 ? "theme" : s;
    }

    private static string Raw(string repo, string branch, string path) =>
        $"https://raw.githubusercontent.com/{repo}/{branch}/{path.TrimStart('/')}";

    private static string? Resolve(MarketItem item, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return path;
        while (path.StartsWith("./")) path = path[2..];
        return Raw(item.Repo, item.Branch, path);
    }

    private static string Hash(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s.ToLowerInvariant())))[..10];

    private static List<MarketItem>? ReadCache(string file, TimeSpan maxAge)
    {
        try
        {
            if (!File.Exists(file) || DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > maxAge) return null;
            return JsonSerializer.Deserialize<List<MarketItem>>(File.ReadAllText(file));
        }
        catch { return null; }
    }

    private static void WriteCache(string file, List<MarketItem> items)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            File.WriteAllText(file, JsonSerializer.Serialize(items));
        }
        catch { }
    }
}
