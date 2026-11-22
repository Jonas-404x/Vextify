namespace Vextify.Core;

/// <summary>Turns Spicetify's raw output into something a human can act on.</summary>
public static class ErrorTranslator
{
    public static string Translate(string raw)
    {
        var t = raw.ToLowerInvariant();

        if (t.Contains("windowsapps") || t.Contains("microsoft store") || t.Contains("spotifyab.spotifymusic"))
            return "Du nutzt die Microsoft-Store-Version von Spotify. Die lässt sich nicht modifizieren. Deinstalliere sie und installiere Spotify von spotify.com.";

        if (t.Contains("prefs") && (t.Contains("not found") || t.Contains("no such file") || t.Contains("cannot find")))
            return "Spotify wurde noch nie gestartet. Starte Spotify einmal, warte kurz und schließe es wieder – dann klappt es.";

        if (t.Contains("mismatch") || (t.Contains("backup") && t.Contains("version")))
            return "Spotify wurde aktualisiert und das alte Backup passt nicht mehr. Ein Klick auf „Reparieren“ erstellt ein neues.";

        if (t.Contains("access is denied") || t.Contains("zugriff") || t.Contains("being used by another process") || t.Contains("used by another"))
            return "Spotify oder ein anderes Programm blockiert die Dateien. Schließe Spotify komplett (auch im Tray) und versuche es erneut.";

        if (t.Contains("no such file") || t.Contains("cannot find the path") || t.Contains("spotify_path"))
            return "Vextify findet deine Spotify-Installation nicht. Ist Spotify installiert?";

        if (t.Contains("timeout") || t.Contains("timed out"))
            return "Der Vorgang hat zu lange gedauert. Versuche es noch einmal.";

        return "Das hat nicht geklappt. Unter „Details“ siehst du die genaue Meldung.";
    }
}
