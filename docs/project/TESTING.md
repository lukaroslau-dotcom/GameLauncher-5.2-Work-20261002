# Tatsächlich ausgeführte Prüfungen – 2026-10-03

Alle Befehle aus der separaten Arbeitskopie; keine Tests im Originalarchiv. Die Dateibelege unter docs/verification enthalten die tatsächlichen Ausgaben. Temporäre Fixturepfade und Ressourcenwerte sind reale Messwerte, keine Zielwerte.

- `dotnet run --project GameLauncher.Tests -c Release -- --process-test <TestGame.exe>`: 120 Kernprüfungen plus sieben echte Prozessprüfungen bestanden, Exit 0. TestGame wird zuvor mit `dotnet build GameLauncher.TestGame -c Release` gebaut. Geprüft: Kindprozess nach Parentende, Doppelstart, Minimieren/Wiederherstellen, einmaliges Sessionende und Checkpoint-Spielzeit.
- `dotnet run --project GameLauncher.Tests -c Release -- --migration-check "C:\Users\shamr\Documents\Visual Studio 18\GameLauncher\Archive\GamesData\games.json"`: zunächst Bytekopie nach Temp, ausschließlich die Kopie importiert. 42/42 Einträge, 0 übersprungen, zweiter Import 0 neu, ursprüngliche Datei identisch. 18 referenzierte Installationsverzeichnisse fehlten. Keine Spielstarts/Save-Schreibzugriffe auf Originalpfade. Dies ist kein vollständiger Medien-/Save-Migrationstest.
- `dotnet run --project GameLauncher.Tests -c Release -- --ui-test --data-root <Temp-Ordner> --fixture-covers <evidence/ui-covers>`: programmgesteuerte WPF-Checks, reale RenderTargetBitmap-Screenshots. Die kleine Coverfixture enthält Kopien lokaler Cover. Netzwerk, automatische Scans und globale Shortcuts deaktiviert. Keine manuelle Maus-/Picker-/Mehrmonitor-Abnahme behauptet.
- `dotnet publish GameLauncher/GameLauncher.csproj -c Release -r win-x64 --self-contained true -o artifacts/GameLauncher-5.2-candidate`: vollständiges Windows-x64-Paket erfolgreich erstellt, Exit 0.
- `./scripts/Test-Published.ps1`: veröffentlichte EXE zweimal in getrennten Temp-Archiven gestartet; sechs echte WPF-Seiten, kontrolliertes Testspiel, Kindprozess, Ruhemodus, Rückkehr, Spielzeit, reguläres Schließen: beide Exit 0. Skript erstellt fixtures, startet keine Originalspiele und hat pro Durchlauf 55 Sekunden Zeitlimit.
- Originalquellen erneut gegen SOURCE-MANIFEST.json geprüft: 58 geprüft, 0 geändert.

## Messung des veröffentlichten Kandidaten

Je Phase ein dreisekündiges CPU-Fenster; vor settledIdle zusätzlich fünf Sekunden Wartezeit. 4 logische Prozessoren. CPU wird als Prozent EINES Kerns angegeben; grobe Gesamtkapazitätsquote = Wert / 4. Keine repräsentative Langzeit-/Großbibliotheksmessung.

| Phase | Lauf 1 CPU / Working Set / Private | Lauf 2 CPU / Working Set / Private |
|---|---|---|
| Startnah | 54,53 % / 140,54 MiB / 77,28 MiB | 70,48 % / 157,00 MiB / 84,96 MiB |
| Nach Ruhephase | 9,35 % / 143,24 MiB / 73,27 MiB | 47,24 % / 163,74 MiB / 88,76 MiB |
| Spielmodus | 2,60 % / 160,95 MiB / 81,67 MiB | 2,08 % / 177,47 MiB / 93,25 MiB |

Spielzeit 16,20 bzw. 16,15 Sekunden. Beide Läufe bestanden die Kindprozess-/Rückkehrbedingungen. UI bleibt resident; kein Nullverbrauch und keine bewiesene generelle RAM-Reduktion. Der stark schwankende Idleverbrauch ist ein offener Performancebefund, keine bestandene Performanceabnahme.

## Nicht ausgeführt / offene Gates

Reale große HTTPS-Transfers und unterbrochene Netzwerkverbindungen; eigene RAR/7z-Fixtures; native manuelle Picker/Drag-drop/GIF-/Mehrmonitorbedienung; Autostart/Registry und LAN-Laufzeitprüfung; volles Medium, Dateisperren, Crash an jeder Transaktionsgrenze; Paketimport; vollständige Parität aller Originalfunktionen. HTTP-Stubs und geometrische DPI-Checks ersetzen diese Prüfungen nicht. Die Umgebung bietet hier keine native interaktive Desktopsteuerung; WPF-Prüfungen sind ausdrücklich programmgesteuert.

Frühere behobene Fehlläufe bleiben im lokalen evidence-Verzeichnis: Compilerfehler während Integration, UI-Erwartung zum deaktivierten Provider und zu großzügige Fuzzy-Suche. Ein älterer 116er-Lauf gilt nicht für die aktuellen Änderungen; maßgeblich ist final-regression-process.txt.

Finaler Build nach reinem Whitespace-Cleanup: `dotnet build GameLauncher/GameLauncher.csproj -c Release`, Exit 0, 0 Warnungen/0 Fehler. `git diff --cached --check` ohne Befund. Keine funktionalen Änderungen nach den oben protokollierten Tests.
