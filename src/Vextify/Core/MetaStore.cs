using System.Text.Json;

namespace Vextify.Core;

public sealed class ItemMeta
{
    public string? Description { get; set; }
    public string? PreviewUrl { get; set; }
    public string? Author { get; set; }
    public string? Repo { get; set; }
    public string? ReadmePath { get; set; }
    public string? Branch { get; set; }
}

/// <summary>
/// What Vextify knows about mods it installed from Entdecken (preview image, longer description, source repo).
/// Locally dropped mods have no entry and simply show what their files say.
/// </summary>
public static class MetaStore
{
    private static readonly object Gate = new();
    private static Dictionary<string, ItemMeta>? _data;
    private static readonly string FilePath = Path.Combine(Paths.VextifyData, "meta.json");

    public static string ThemeKey(string name) => "theme:" + name;
    public static string ExtKey(string file) => "ext:" + file;

    public static ItemMeta? Get(string key)
    {
        lock (Gate) { Load(); return _data!.TryGetValue(key, out var m) ? m : null; }
    }

    public static void Set(string key, ItemMeta meta)
    {
        lock (Gate)
        {
            Load();
            _data![key] = meta;
            try
            {
                Directory.CreateDirectory(Paths.VextifyData);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { Log.Write("MetaStore: " + ex.Message); }
        }
    }

    private static void Load()
    {
        if (_data != null) return;
        try { _data = File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, ItemMeta>>(File.ReadAllText(FilePath)) : null; }
        catch { _data = null; }
        _data ??= new();
    }
}
