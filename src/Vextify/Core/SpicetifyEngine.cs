using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Vextify.Core;

public sealed record EngineResult(bool Ok, string Output)
{
    public string Friendly => Ok ? "" : ErrorTranslator.Translate(Output);
}

/// <summary>
/// V1 patching engine: drives the official spicetify.exe. Everything the rest of the app needs goes through
/// this class, so it can later be replaced by a native patcher without touching the UI.
/// </summary>
public sealed class SpicetifyEngine
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Vextify");
        return c;
    }

    public bool IsInstalled => File.Exists(Paths.SpicetifyExe);

    public async Task<string?> GetVersionAsync()
    {
        if (!IsInstalled) return null;
        var r = await RunAsync("-v", TimeSpan.FromSeconds(15));
        return r.Ok ? r.Output.Trim().Split('\n').Last().Trim() : null;
    }

    /// <summary>Downloads the latest official spicetify release into the standard location.</summary>
    public async Task<EngineResult> InstallAsync(IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report("Suche aktuelle Spicetify-Version …");
            using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://api.github.com/repos/spicetify/cli/releases/latest"));
            var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            var asset = doc.RootElement.GetProperty("assets").EnumerateArray()
                .FirstOrDefault(a => a.GetProperty("name").GetString()!.EndsWith($"windows-{arch}.zip", StringComparison.OrdinalIgnoreCase));
            if (asset.ValueKind == JsonValueKind.Undefined)
                return new EngineResult(false, "Kein passender Spicetify-Download gefunden.");

            var url = asset.GetProperty("browser_download_url").GetString()!;
            progress?.Report("Lade Spicetify herunter …");
            var tmp = Path.Combine(Path.GetTempPath(), "vextify-spicetify-" + Guid.NewGuid().ToString("N") + ".zip");
            await using (var s = await Http.GetStreamAsync(url))
            await using (var f = File.Create(tmp))
                await s.CopyToAsync(f);

            progress?.Report("Entpacke …");
            Directory.CreateDirectory(Paths.SpicetifyExeDir);
            ZipFile.ExtractToDirectory(tmp, Paths.SpicetifyExeDir, overwriteFiles: true);
            File.Delete(tmp);

            if (!IsInstalled) return new EngineResult(false, "spicetify.exe fehlt nach dem Entpacken.");
            await EnsureConfigAsync();
            return new EngineResult(true, "");
        }
        catch (Exception ex)
        {
            Log.Write("Engine-Install: " + ex);
            return new EngineResult(false, "Download fehlgeschlagen: " + ex.Message);
        }
    }

    /// <summary>Spicetify creates its config on first run; make sure that has happened.</summary>
    public async Task EnsureConfigAsync()
    {
        if (!File.Exists(Paths.SpicetifyConfigIni)) await RunAsync("config", TimeSpan.FromSeconds(20));
    }

    /// <summary>Backup (if needed) and apply. This is the one-shot "make Spotify modded" operation.</summary>
    public async Task<EngineResult> ApplyAsync()
    {
        if (!IsInstalled) return new EngineResult(false, "Spicetify ist nicht installiert.");
        await EnsureConfigAsync();
        var r = await RunAsync("backup apply", TimeSpan.FromMinutes(3));
        if (!r.Ok && r.Output.Contains("backup", StringComparison.OrdinalIgnoreCase))
        {
            // stale backup after a Spotify update: let Spicetify rebuild it
            Log.Write("backup apply failed, retrying after clear: " + r.Output);
            var c = await RunAsync("restore backup apply", TimeSpan.FromMinutes(3));
            if (c.Ok) return c;
        }
        return r;
    }

    public Task<EngineResult> RestoreAsync() => RunAsync("restore", TimeSpan.FromMinutes(2));

    public async Task<EngineResult> RunAsync(string args, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(Paths.SpicetifyExe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Paths.SpicetifyExeDir,
        };
        try
        {
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(timeout);
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { p.Kill(true); } catch { }
                return new EngineResult(false, "timeout: spicetify " + args);
            }
            var output = Clean(StripAnsi((await stdout) + (await stderr))).Trim();
            Log.Write($"spicetify {args} -> exit {p.ExitCode}\n{output}");
            return new EngineResult(p.ExitCode == 0 && !LooksLikeError(output), output);
        }
        catch (Exception ex)
        {
            Log.Write("spicetify " + args + " failed to start: " + ex.Message);
            return new EngineResult(false, ex.Message);
        }
    }

    // Spicetify sometimes prints errors but still exits 0.
    private static bool LooksLikeError(string output) =>
        output.Contains("error", StringComparison.OrdinalIgnoreCase) && output.Contains("fatal", StringComparison.OrdinalIgnoreCase);

    /// <summary>Spicetify prints spinner frames and a progress bar per step; keep the readable lines and only the last bar.</summary>
    private static string Clean(string s)
    {
        var result = new List<string>();
        string? lastBar = null;
        foreach (var raw in s.Replace("\r", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^[\\|/\-]\s")) continue;   // spinner frame
            if (line.Contains('█')) { lastBar = line; continue; }                                // progress bar
            if (lastBar != null) { result.Add(lastBar); lastBar = null; }
            result.Add(line);
        }
        if (lastBar != null) result.Add(lastBar);
        return string.Join("\n", result);
    }

    private static string StripAnsi(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, @"\x1B\[[0-9;]*[A-Za-z]", "");
}
