namespace Vextify.Core;

/// <summary>
/// Minimal line-preserving INI editor for Spicetify's config-xpui.ini. Only touches the one key we change
/// so everything else (comments, ordering, keys we don't know) stays exactly as Spicetify wrote it.
/// </summary>
public sealed class IniFile
{
    private readonly string _path;
    private List<string> _lines = new();

    public IniFile(string path)
    {
        _path = path;
        if (File.Exists(path)) _lines = File.ReadAllLines(path).ToList();
    }

    public bool Exists => File.Exists(_path);

    public string? Get(string section, string key)
    {
        var (start, end) = SectionRange(section);
        if (start < 0) return null;
        for (var i = start + 1; i < end; i++)
        {
            var (k, v) = Split(_lines[i]);
            if (k != null && k.Equals(key, StringComparison.OrdinalIgnoreCase)) return v;
        }
        return null;
    }

    public void Set(string section, string key, string value)
    {
        var (start, end) = SectionRange(section);
        if (start < 0)
        {
            _lines.Add($"[{section}]");
            _lines.Add($"{key} = {value}");
            return;
        }
        for (var i = start + 1; i < end; i++)
        {
            var (k, _) = Split(_lines[i]);
            if (k != null && k.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                _lines[i] = $"{k} = {value}";
                return;
            }
        }
        // insert before trailing blank lines of the section
        var at = end;
        while (at - 1 > start && string.IsNullOrWhiteSpace(_lines[at - 1])) at--;
        _lines.Insert(at, $"{key} = {value}");
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllLines(_path, _lines);
    }

    private (int start, int end) SectionRange(string section)
    {
        var start = _lines.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
        if (start < 0) return (-1, -1);
        var end = _lines.FindIndex(start + 1, l => l.TrimStart().StartsWith('['));
        return (start, end < 0 ? _lines.Count : end);
    }

    private static (string? key, string? value) Split(string line)
    {
        var t = line.Trim();
        if (t.Length == 0 || t[0] == ';' || t[0] == '#' || t[0] == '[') return (null, null);
        var eq = t.IndexOf('=');
        if (eq < 0) return (null, null);
        return (t[..eq].Trim(), t[(eq + 1)..].Trim());
    }
}
