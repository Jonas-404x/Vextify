namespace Vextify.Core;

public sealed record Status(
    bool SpotifyInstalled,
    bool StoreVersion,
    string? SpotifyVersion,
    bool SpotifyRunning,
    bool EngineInstalled,
    bool Patched,
    bool Busy,
    string BusyText,
    string? LastError,
    string? LastErrorDetails);

/// <summary>
/// The heart of Vextify: keeps Spotify patched. Polls Spotify's state, re-applies the mod after updates or
/// restarts, and serialises every operation so the UI and the background watcher never fight each other.
/// </summary>
public sealed class Guardian
{
    public static Guardian Instance { get; } = new();

    public SpicetifyEngine Engine { get; } = new();
    public ModService Mods { get; } = new();

    public event Action<Status>? StatusChanged;
    public event Action<string, string>? Notify;   // (title, message)

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;
    private bool _busy;
    private string _busyText = "";
    private string? _lastError, _lastDetails;
    private DateTime _lastAutoAttempt = DateTime.MinValue;
    private string? _lastAutoAttemptVersion;
    private Status _last = null!;

    /// <summary>Mods/theme were changed in the UI but not applied to Spotify yet.</summary>
    public bool PendingChanges { get; set; }

    public Status Current => _last ??= Snapshot();

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop() => _cts?.Cancel();

    private Status Snapshot() => new(
        SpotifyInfo.IsInstalled, !SpotifyInfo.IsInstalled && SpotifyInfo.IsStoreVersionPresent, SpotifyInfo.Version, SpotifyInfo.IsRunning,
        Engine.IsInstalled, IsPatched(), _busy, _busyText, _lastError, _lastDetails);

    // Two independent signals: Spicetify's marker in Spotify's bundle, or "nothing changed since our last successful apply".
    // The second one keeps us from re-patching (and restarting Spotify) in a loop if the marker check is ever wrong.
    private static bool IsPatched() =>
        SpotifyInfo.IsPatched() ||
        (Settings.Current.PatchedSignature.Length > 0 && Settings.Current.PatchedSignature == SpotifyInfo.Signature());

    /// <summary>Forced: always tells the UI (used after the user changes something).</summary>
    public void Refresh()
    {
        var s = Snapshot();
        _last = s;
        StatusChanged?.Invoke(s);
    }

    /// <summary>Background poll: only wakes the UI when something actually changed.</summary>
    private void Poll()
    {
        var s = Snapshot();
        if (s == _last) return;
        _last = s;
        StatusChanged?.Invoke(s);
    }

    private volatile bool _windowVisible = true;

    /// <summary>The window is the only consumer of fast updates; while it sits in the tray we poll slowly.</summary>
    public void SetWindowVisible(bool visible) => _windowVisible = visible;

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Poll();
                await MaybeAutoRepairAsync();
            }
            catch (Exception ex) { Log.Write("Guardian-Loop: " + ex); }
            try { await Task.Delay(TimeSpan.FromSeconds(_windowVisible ? 5 : 20), ct); } catch (OperationCanceledException) { }
        }
    }

    private async Task MaybeAutoRepairAsync()
    {
        var s = Current;
        if (!Settings.Current.Activated || !Settings.Current.AutoReapply || s.Busy || !s.SpotifyInstalled || s.Patched) return;
        if (!SpotifyInfo.HasPrefs) return;      // Spotify never started: nothing to patch yet

        // Don't hammer: after a failed attempt on this Spotify version wait a few minutes before the next try.
        var version = s.SpotifyVersion;
        if (_lastAutoAttemptVersion == version && DateTime.UtcNow - _lastAutoAttempt < TimeSpan.FromMinutes(5)) return;
        _lastAutoAttempt = DateTime.UtcNow;
        _lastAutoAttemptVersion = version;

        // Spotify is still updating itself right after launch – give it a moment to settle.
        if (s.SpotifyRunning) await Task.Delay(TimeSpan.FromSeconds(8));

        Notify?.Invoke("Vextify", "Spotify wurde aktualisiert – Mods werden wieder aktiviert …");
        var ok = await RepairAsync(restartIfRunning: Settings.Current.AutoRestartSpotify);
        Notify?.Invoke("Vextify", ok ? "Mods sind wieder aktiv." : "Reparatur fehlgeschlagen. Öffne Vextify für Details.");
    }

    /// <summary>Install the engine if missing, then (re)apply the mod. Safe to call from anywhere.</summary>
    public async Task<bool> RepairAsync(bool restartIfRunning = true)
    {
        await _gate.WaitAsync();
        try
        {
            SetBusy(true, "Bereite alles vor …");
            _lastError = _lastDetails = null;

            if (!SpotifyInfo.IsInstalled)
                return Fail(SpotifyInfo.IsStoreVersionPresent
                    ? ErrorTranslator.Translate("windowsapps")
                    : "Spotify wurde nicht gefunden. Installiere die Desktop-Version von spotify.com.", "");

            if (!SpotifyInfo.HasPrefs)
                return Fail("Spotify wurde noch nie gestartet. Starte Spotify einmal, warte kurz und schließe es wieder.", "");

            if (!Engine.IsInstalled)
            {
                SetBusy(true, "Lade Spicetify herunter …");
                var install = await Engine.InstallAsync(new Progress<string>(t => SetBusy(true, t)));
                if (!install.Ok) return Fail("Spicetify konnte nicht geladen werden. Prüfe deine Internetverbindung.", install.Output);
            }

            var wasRunning = SpotifyInfo.IsRunning;
            if (wasRunning)
            {
                SetBusy(true, "Beende Spotify …");
                await Task.Run(SpotifyInfo.Kill);
                await Task.Delay(1000);
            }

            SetBusy(true, "Wende Mods an …");
            var result = await Engine.ApplyAsync();

            if (wasRunning && restartIfRunning)
            {
                SetBusy(true, "Starte Spotify …");
                SpotifyInfo.Start();
            }

            if (!result.Ok) return Fail(result.Friendly, result.Output);

            Settings.Current.PatchedSignature = SpotifyInfo.Signature();
            Settings.Current.Activated = true;
            Settings.Current.Save();
            PendingChanges = false;
            return true;
        }
        finally
        {
            SetBusy(false, "");
            _gate.Release();
            Refresh();
        }
    }

    /// <summary>Remove the mod and bring back stock Spotify.</summary>
    public async Task<bool> RestoreAsync()
    {
        await _gate.WaitAsync();
        try
        {
            SetBusy(true, "Stelle Original-Spotify wieder her …");
            _lastError = _lastDetails = null;
            var wasRunning = SpotifyInfo.IsRunning;
            if (wasRunning) { await Task.Run(SpotifyInfo.Kill); await Task.Delay(1000); }
            var r = await Engine.RestoreAsync();
            if (wasRunning) SpotifyInfo.Start();
            if (!r.Ok) return Fail(r.Friendly, r.Output);

            // The user asked for stock Spotify: don't silently re-patch it. "Reparieren" turns this back on.
            Settings.Current.AutoReapply = false;
            Settings.Current.PatchedSignature = "";
            Settings.Current.Save();
            return true;
        }
        finally { SetBusy(false, ""); _gate.Release(); Refresh(); }
    }

    public async Task RestartSpotifyAsync()
    {
        await Task.Run(SpotifyInfo.Kill);
        await Task.Delay(800);
        SpotifyInfo.Start();
        Refresh();
    }

    private bool Fail(string friendly, string details)
    {
        _lastError = friendly;
        _lastDetails = details;
        Log.Write("Fehler: " + friendly + " | " + details);
        return false;
    }

    private void SetBusy(bool busy, string text)
    {
        _busy = busy;
        _busyText = text;
        Refresh();
    }
}
