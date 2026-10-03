# Aktueller Stand – 2026-10-03

Arbeitskopie: `C:\Users\shamr\Documents\GameLauncher-5.2-Work-20261002`, Branch `codex/5.2-integration`, Originalsnapshot `6e9d17d`. Der Integrationscommit ist über die letzte AI_CHANGELOG-Überschrift auffindbar. Originalquellen erneut SHA-geprüft: 58/58 unverändert. Die echte games.json wurde für einen Migrationstest nur gelesen und byteidentisch nach Temp kopiert; ihr Hash blieb unverändert.

## Implementiert und verbunden

WPF/.NET 10 erhalten; Next als Referenz. Isolierter Standarddatenordner und getrennte Autostart-/Shortcutnamen. Statusänderungen erhalten Installation/Saves. SHA-256-Backups, aktuelle plus eine History, ältere in Retired; manuell/vor/nach Spiel/Intervall; sicherer Restore in neuen Ordner. Installationsentfernung mit Receipt und verifizierter Wiederherstellung. HTTPS-Downloads mit persistierten Jobs, Pause/Resume/Abbruch/Hash; managed und abbrechbare Archive, Originale erhalten. ID-bewahrender Installationsverlauf und explizite Versionsauswahl. Settingsimport mit Hash/Snapshot und erhaltener lokaler Autorität. Bibliotheks-ZIP-Export mit Byteprüfung und Abbruch. Lokale Aliase/Tags/Tippfehler, Wishlist, größere Hero-/Coverdarstellung, maximal fünf 16:9-Karten, violettes Dark-Theme, verbundene Downloads/Backups und korrigierte untere Navigation.

## Tatsächliche finale Prüfläufe

- 120 Kernchecks + 7 echte Prozesschecks bestanden, Exit 0.
- 48 programmgesteuerte WPF-Checks bestanden, Exit 0; Screenshots erzeugt. 1000/1366 Layouts sowie geometrische DPI-/Monitorchecks, keine manuelle Mehrmonitorbehauptung.
- Migration einer echten JSON-Kopie: 42 übernommen, Wiederimport 0 neu, 0 übersprungen, Original unverändert. 18 Installationspfade fehlen; keine Spiele aus dieser Bibliothek gestartet.
- Vollständiger self-contained win-x64-Publish. Zweimal veröffentlichte EXE: sechs Seiten, Kindprozess, Spielmodus, Rückkehr und Spielzeit bestanden, Exit 0.
- Ressourcen gemessen (4 logische CPUs): nach kurzer Ruhephase 9,35/47,24 % eines Kerns; Spielmodus 2,60/2,08 %. Private RAM im Spielmodus 81,67/93,25 MiB. Kein Nullverbrauch, keine Performancefreigabe.

Belege: docs/verification und TESTING.md. Paket: artifacts/GameLauncher-5.2-candidate. Testdaten ausschließlich Temp/evidence. Aktuelle Funktionsmatrix: FEATURE_MATRIX.md.

## Offen / keine Releasefreigabe

Der Handoff-Gesamtauftrag ist noch nicht vollständig erfüllt. Fehlend sind insbesondere austauschbare Online-Suchprovider mit Offlinecache, Mehrsignal-Downloadzuordnung, Publisher-Feedpipeline, vollständig journalisierte Updates/Moves, Paketimport, konfigurierbare Homebereiche und eine vollständige Original-Funktionsabnahme. Der variable Idleverbrauch erfordert Profiling. Native interaktive UI-Steuerung ist hier nicht verfügbar; Picker/Drag-drop/Autostart/LAN/echte Mehrmonitore sind ungetestet. Ebenso offen: große reale Netztransfers, RAR/7z-Formatfixtures, Crash-/Sperren-/Datenträgervollmatrix. Diese Grenzen sind keine bestandenen Gates und werden nicht durch Build/Unit-Tests ersetzt.

Keine Originaldaten automatisch migriert, keine Veröffentlichung/kein GitHub-Push, kein signierter Installer. Die aktuell ausführbare Ausgabe ist ausdrücklich ein Testkandidat.
