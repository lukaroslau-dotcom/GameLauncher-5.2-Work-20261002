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

## Finale Revision: Smart Catalog / Medien / Paketimport

Die vorstehenden Ergebnisse dokumentieren den früheren Kandidaten b5ed80f. Für die aktuelle Revision sind die folgenden erneut ausgeführten Gates und `docs/verification/revision-*` maßgeblich:

- `dotnet run --project GameLauncher.Tests -c Release -- --process-test "C:\Users\shamr\Documents\GameLauncher-5.2-Work-20261002\GameLauncher.TestGame\bin\Release\net10.0-windows\GameLauncher.TestGame.exe"`: 133 Kernchecks + sieben Prozesschecks, Exit 0.
- `dotnet run --project GameLauncher.Tests -c Release -- --ui-test --data-root <neuer Tempordner> --fixture-covers <evidence/ui-covers>`: 59 Checks, Exit 0. Enthält simuliertes Mausrad, Katalogdialog, Wunschliste ohne Quelle/Version, Galerie/Diagnose und gemeinsames Updatescrollen.
- `dotnet run --project GameLauncher.Tests -c Release -- --catalog-online-check <Suchname>`: echter F95CatalogProvider/öffentlicher HTTP-Client, 23 Treffer ohne Anmeldung. Keine Account-/Downloadlinkprüfung.
- `dotnet publish GameLauncher/GameLauncher.csproj -c Release -r win-x64 --self-contained true -o artifacts/GameLauncher-5.2-smart-candidate`: Exit 0.
- `./scripts/Test-Published.ps1 -Package ./artifacts/GameLauncher-5.2-smart-candidate`: zwei tatsächliche EXE-Starts, je 14 Checks, beide Exit 0. Neu ausdrücklich Galerie und Diagnose schließen, Hauptfenster bleibt offen.
- 58 Originalquelldateien erneut gegen SOURCE-MANIFEST.json identisch.

Neue Messung: Lauf 1 settledIdle 0,519 % eines Kerns, Working Set 182149120 Bytes, Private 139153408 Bytes; Gaming 8,295 %, 210112512 / 156499968 Bytes. Lauf 2 settledIdle 0 %, 173268992 / 134180864 Bytes; Gaming 7,256 %, 201244672 / 152248320 Bytes. Drei Sekunden Messfenster, vier logische CPUs. Spielzeit 15,401 / 16,170 Sekunden. Kein Langzeitbenchmark; Galerieprüfung verändert den Speicherverlauf. Frühere schwankende Werte werden nicht unterschlagen.

Paketimport ist inzwischen mit isoliertem Manifest-/Hash-/Settings-Test abgedeckt; die frühere Aussage „Paketimport nicht ausgeführt“ gilt für b5ed80f. Alle anderen oben genannten nicht ausgeführten Gates bleiben offen. Ein Dialog-Ressourcenpfad scheiterte zwischenzeitlich im UI-Test; absolute Component-URI korrigiert, finale UI- und EXE-Gates danach bestanden.
