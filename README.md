# Vextify

Themes und Mods für Spotify am PC, ohne dass du irgendwas in PowerShell tippen musst.

Ich hab mich ständig mit Spicetify rumgeärgert: Installation schmiert ab, nach jedem Neustart muss ein Befehl her, nach jedem Spotify-Update ist alles weg. Vextify macht das alles selbst. Du klickst einmal auf "Mods aktivieren" und danach bleibt es einfach drin.

## Was es kann

- **Ein Klick statt Konsole.** Vextify lädt die Mod-Engine, sichert Spotify und wendet alles an.
- **Bleibt aktiv.** Läuft im Tray, startet mit Windows und erneuert die Mods von selbst, wenn Spotify sie bei einem Update überschrieben hat.
- **Entdecken.** Themes und Extensions aus der Spicetify-Community, mit Vorschaubildern, Suche und Installation per Klick.
- **Mods verwalten.** An- und ausschalten, Details ansehen, eigene `.js`-, `.css`- oder `.zip`-Dateien einfach ins Fenster ziehen.
- **Verständliche Fehler.** Statt kryptischer Konsolenausgabe steht da, was los ist und was du tun kannst.
- **Sparsam.** Im Tray fragt Vextify nur alle paar Sekunden nach, und Mods mit aufwendigen Effekten (Blur, Dauer-Animationen) werden markiert.

## Loslegen

1. Spotify von [spotify.com](https://www.spotify.com/download/windows/) installieren. **Nicht** die Microsoft-Store-Version, die lässt sich nicht modifizieren.
2. Spotify einmal öffnen, anmelden, wieder schließen.
3. `Vextify.exe` aus den [Releases](../../releases) laden und starten.
4. Auf "Mods aktivieren" klicken.

Windows meldet beim ersten Start evtl. SmartScreen, weil die App nicht signiert ist. "Weitere Informationen" und dann "Trotzdem ausführen".

## Stand

Das ist eine frühe Version (0.1). Das Aktivieren und die automatische Reparatur laufen. Community-Themes aus Entdecken sind noch nicht auf allen Rechnern getestet, wenn was nicht klappt, mach gern ein Issue auf und häng den Text aus "Details" auf der Startseite dran.

## Selber bauen

Du brauchst das .NET 8 SDK.

```powershell
dotnet build src\Vextify -c Release
```

Eine einzelne `.exe` zum Weitergeben:

```powershell
dotnet publish src\Vextify -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

Mit der Datei-Komprimierung (`EnableCompressionInSingleFile`) wird die `.exe` kleiner, braucht aber rund 125 MB mehr Arbeitsspeicher. Deshalb ist sie hier aus.

## Gut zu wissen

- Vextify verändert Dateien von Spotify. Das kann gegen die Nutzungsbedingungen von Spotify verstoßen. Nutzung auf eigene Verantwortung.
- Einen Werbeblocker gibt es bewusst nicht.
- Die Mod-Engine ist [Spicetify](https://github.com/spicetify/cli) (LGPL-2.1). Vextify lädt sie beim ersten Aktivieren selbst herunter und liefert sie nicht mit. Der Katalog in Entdecken kommt aus der Spicetify-Community.
- Gebaut mit WPF und [WPF-UI](https://github.com/lepoco/wpfui).
