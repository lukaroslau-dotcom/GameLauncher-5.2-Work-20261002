# Architektur des 5.2-Integrationskandidaten

Die bestehende WPF-Anwendung bleibt ein Prozess mit nativen C#-Diensten und JSON-Dateien. Der vorhandene Prozessbaum-Monitor und die LAN-Funktion bleiben erhalten. Kein zweiter Datenwriter und kein neuer Server wurden eingeführt. Das Next-Host-/SQLite-Modell wurde nicht parallel daneben gestellt.

Lange Dateiarbeit läuft über eine gemeinsame Busy-Sperre außerhalb des UI-Threads. Scans pausieren; Updateprüfungen starten nicht gleichzeitig. Archivvorbereitung und Bibliotheksexport haben CancellationTokens; atomare Veröffentlichungsschritte werden nicht mitten im Rename unterbrochen.

Downloads liegen in GUID-Unterordnern unter dem Datenarchiv, höchstens drei gleichzeitig. HTTPS, öffentliche Zieladressen, kein automatischer Redirect, Größenlimit, ETag/Range und optional erwarteter SHA-256 begrenzen die Übertragung. Ein Neustart lädt Aufträge pausiert. Der Spielmodus pausiert aktive Downloads.

Savebackups werden in einem neuen Stagingordner kopiert und gegen die Quelle gehasht. Erst danach wird die aktuelle Sicherung ersetzt; historische Sicherungen werden nicht ungeprüft gelöscht. Installationsentfernung schreibt eine Recoveryreceipt vor dem Rename auf demselben Volume. Restore prüft Inhalt und freien ursprünglichen Zielpfad. Vorhandene Installationsverschiebungen sind noch nicht durchgehend als persistente Transaktionen modelliert.

JSON-Import bewahrt Originalbytes, überprüft den Vorschauhash, normalisiert Daten und bewahrt IDs/Erweiterungsfelder. Einstellungsimport schützt lokale Pfade, Startautorität und aktive Netzwerkparameter. Einstellungen und Library verwenden atomare Dateiersetzung mit Recoverykopien. Das ZIP-Exportformat enthält Bibliothek, Medien, aktive/historische/retired Saves und Einstellungen samt Dateihashes. Installationen/Downloads sind ausgeschlossen. Paketimport prüft das Manifest und erstellt eine neue Kopie; importierte Einstellungen bleiben inaktiv. sourceRoot ermöglicht die Medienpfadzuordnung bei Exporten dieser Revision.

Grenzen: Der WPF-Prozess bleibt im Spielmodus resident. Keine Job-Object-Garantie oder crashübergreifende Sessionaufnahme. Kein signierter Installer. Kein vollständiges Audit der geerbten LAN-/Providerfunktion.

Suchprovider liegen hinter IGameCatalogProvider und optional ICatalogAccountProvider. CatalogProviders.Create ist die verbundene Factory; keine dynamische Pluginladung oder implementierte Anmeldung. SmartAssociation verknüpft nur eindeutig gemerkte lokale Titel, während Metadatenanreicherung manuelle Feldmarkierungen respektiert. Seit 04.10. sind Katalogsuche eine Hauptfensterseite und Galerie/Zuordnung eingebettete Overlays. Diagnose bleibt ein untergeordnetes WPF-Fenster. App beendet sich am Hauptfenster.
