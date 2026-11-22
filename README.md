# Vextify

Spotify-Mods für Windows – als normale App statt PowerShell-Skripten.

Vextify aktiviert Themes und Extensions in der Spotify-Desktop-App und hält sie aktiv: nach jedem
Neustart und nach jedem Spotify-Update. Du musst dafür nichts mehr in die Konsole tippen.

## Status

Frühe Version (0.1). Die Oberfläche steht, die Mod-Engine ist in V1 das offizielle
[Spicetify](https://github.com/spicetify/cli), das Vextify beim ersten Aktivieren selbst herunterlädt.

## Funktionen (V1)

- Fenster im Windows-11-Stil mit Start, Mods, Themes und Einstellungen
- Ein Klick auf „Mods aktivieren“: Vextify lädt die Engine, sichert Spotify und wendet die Mods an
- Läuft im Tray und erneuert die Mods automatisch, wenn Spotify sie bei einem Update überschrieben hat
- Verständliche Fehlermeldungen statt Konsolen-Output (Details auf Wunsch)
- Hinweis mit Download-Link, wenn Spotify fehlt oder die Microsoft-Store-Version installiert ist
- Eigene `.js`-, `.css`- und `.zip`-Mods per Drag-and-drop

## Voraussetzungen

Die Desktop-Version von Spotify von [spotify.com](https://www.spotify.com/download/windows/),
nicht die aus dem Microsoft Store. Die Store-Version lässt sich nicht modifizieren.

## Bauen

```powershell
cd src\Vextify
dotnet build -c Release
```

Einzelne .exe zum Weitergeben:

```powershell
dotnet publish src\Vextify -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

## Hinweise

- Vextify verändert die Dateien einer fremden Anwendung. Das kann gegen die Nutzungsbedingungen von
  Spotify verstoßen. Nutzung auf eigene Verantwortung.
- Vextify enthält bewusst keinen Werbeblocker.
- Vor einer Weitergabe prüfen: Lizenz von Spicetify, Code-Signing (sonst warnt Windows SmartScreen).
