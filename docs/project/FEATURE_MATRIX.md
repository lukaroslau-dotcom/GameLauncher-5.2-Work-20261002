# Abnahmematrix 5.2 / Next / Integration

Stand 2026-10-03. „Vorhanden“ bezeichnet Quellinventar, nicht automatisch einen vollständig ausgeführten Benutzertest. Next-Funktionen basieren auf dem Handoff; dessen 85 Tests gelten nicht für diese Anwendung.

| Bereich | 5.2 vorher | Next-Referenz | Aktuelle Integration / Test | Rest / Abnahmegrenze |
|---|---|---|---|---|
| 1 Suche | Lokale Suche, bestehende Metadatenanbindung | Providervertrag, Wikipedia, Cache, Aliase | Aliase/Tags/Abkürzungen/Tippfehler; exakte Treffer vor unscharfen; Regression/UI | Austauschbare Onlineprovider, Cache/offline und reiche Suchvorschau nicht portiert |
| 2 Erkennung | Ordnerscan, Watcher, EXE-/Archivkandidaten | Begrenzter Inspector, Bestätigung | Erhalten; Dateiarbeit/Spielen pausieren Scans | Vollständiger realer Ordner-/Watcher-E2E fehlt |
| 3 Zuordnung | Bestehender manueller Add-/Reinstallflow | Score/Gründe aus mehreren Signalen | Downloads gehen ausdrücklich in Add/Archivprüfung; keine automatische Namensinstallation | Mehrsignal-Confidence nicht portiert |
| 4 Downloads/Archive | Externes Entpacken mit Originallöschung | HTTPS/Resume/Hash/Managed Archive | Persistierte Jobs, Pause/Resume/Abbruch, Range/ETag, SHA, Grenzen, ZIP/RAR/7z; Original bleibt; Abbruchknopf | HTTP mit Stubs geprüft; kein realer großer Netzwerktransfer; RAR/7z keine eigenen Formatfixtures |
| 5 Bibliothek | Status/Favoriten/Filter/Sortierung/Kontext/Multiselect | Wishlist und Tags | Wishlist ergänzt, Status unabhängig von Dateien; maximal fünf 16:9-Cover; UI-Prüfung | Breite Genre-/Engine-/Dimensionsfilter nicht erweitert |
| 6 Home | Hero/Recent/Statistik | Favorites/Smart Mix/konfigurierbar | Größerer Hero, Favoriten, lokaler Mix, fünf Karten; echte lokale Werte | Reihenfolge nicht konfigurierbar; leere Bibliothek hat keinen fiktiven Hero |
| 7 Detail | Cover/Crop/Galerie/Quelle/Notizen/Status | Advanced/Trailer | Argumente/Aliase/Tags, ältere Installation auswählen, vorhandene Detailaktionen erhalten | Trailer und Metadatenherkunft nicht vollständig erweitert |
| 8 Add/Edit | Picker/Drop/Erkennung/Reinstall | atomare ID-/Duplikatflows | Bestehende ID bei Reinstall, Historienzeiger; Form- und Duplikatregressionen | Native Picker/Drag-drop nicht manuell geprüft; bestehende Move-Transaktionen nicht vollständig journalisiert |
| 9 Updates | Vorhandene Updateprüfung/Neuinstallation | Publisher-JSON, Staging, bestätigter Wechsel | Bestehende Anbindung bleibt; Verlauf und bestätigter Versionswechsel, Savevorabsicherung | Publisher-Feedpipeline und durchgehend crashfeste Updatejournale fehlen |
| 10 Entfernen | Status konnte Installationsstatus ändern; endgültige Dateienentfernung | Receipt/Recovery | Status nur Status; Installation per SHA-Receipt umbenennen, Restore ohne Überschreiben; Tests occupied/tamper/restart | Keine automatische Papierkorblöschung; explizite Savearchiv-Löschung weiterhin separat destruktiv |
| 11 Backups | Savearchiv/History/Restore | SHA, zwei aktiv, Retired, Automation | SHA-Manifest, aktuelle+eine History, ältere Retired; manuell/Start/Ende/Intervall; Restore in neuen Ordner | Auswahl aller historischen Stände nicht vollständig als eigener Browser; kein freier Backupzielpicker |
| 12 Prozesse/Gaming | Native Prozessbaumverfolgung, Tray, Spielzeit | Persistenter Host/Job Objects | Vorhandenes Modell erhalten, Downloadpause; tatsächlicher EXE-/Kindprozess-/Rückkehrtest und Messung | Kein kleiner separater Host; UI bleibt im RAM; kein Crash-Reattach; externe Launchergrenzen |
| 13 Settings/Erststart | Setup, Pfade, Shortcuts, LAN, Darstellung | Transfer/Diagnose | Atomare Settings, korrupt blockiert, hash-/snapshot-geprüfter Transfer, lokale Autorität erhalten; strukturierte Logs | Registry-/Autostart- und LAN-E2E nicht ausgeführt |
| 14 Migration/Export | JSON-Import/Export und Ordnerwechsel | Vollständiges Format-2-Paket | Vorschau/Originalbytes/Idempotenz; SHA-geprüfter Ordnercopy; ZIP-Export mit Manifest; echte JSON-Kopie: 42 rein, Wiederimport 0, Original unverändert | Paketimport und volle Medien/Save-Migration mit echten Daten nicht abgenommen; 18 Originalinstallationen im Test nicht vorhanden |
| 15 Diagnose | Debug/Fehlermeldungen | Strukturierte Logs/Recovery | JSONL, Startupfehler, Backupprüfung, Recoveryliste, reale Startbelege | Kein vollständiges Diagnosezentrum; Crash/volle Platte/Sperren/Logrotation noch nicht vollständig getestet |

Erhaltene Sonderfunktionen: vorhandene Galerie/GIF/Crop, F95-Metadaten-/Updatefunktion, LAN-Streaming, Setup, globale Shortcuts, Tray, Mehrfachauswahl. Sie wurden nicht durch Ersatzattrappen entfernt. Erhalt im Quellcode ist keine pauschale Laufzeitabnahme.

Die Bibliotheksanforderung aus dem Handoff („ungefähr sechs“) wurde durch die jüngere Nutzeranweisung („fünf mittelgroße“) ersetzt. Die sechs Bilder dienen der Gestaltung und enthalten keine echten Statistiken dieser Installation.
