# Bestätigte Projektbasis

Stand 2026-10-03. Separate Kopie: `C:\Users\shamr\Documents\GameLauncher-5.2-Work-20261002`, Branch `codex/5.2-integration`, Basissnapshot `6e9d17d`.

58 Quell-/Projektdateien wurden vom lokalen Originalprojekt übernommen und über SOURCE-MANIFEST.json mit SHA-256 dokumentiert. Erneute Prüfung am 03.10.: alle 58 unverändert im Original. Das Originalprojekt ist WPF/.NET 10, Version 5.2.0. Die Architekturentscheidung, 5.2 beizubehalten, wurde nach Erläuterung des Konflikts mit der separaten V7-Architektur durch die Entscheidungsfreigabe des Nutzers gedeckt. V7 wurde nicht verändert.

Native C#-Dateidienste, JSON-Persistenz und vorhandene WPF-Oberfläche bleiben die Produktbasis. Kein zweiter Writer, keine neue Weboberfläche, kein neuer lokaler Server. Die bereits vorhandene LAN-Funktion bleibt enthalten. Kein SQLite-/Host-/Job-Object-Port aus Next wird behauptet.

Neue Felder erhalten unbekannte JSON-Eigenschaften. Native Pfadvalidierung und SHA-256 schützen die neuen Backup-/Archiv-/Recovery-Abläufe. Ein isolierter Standarddatenordner verhindert implizite Schreibzugriffe auf das ursprüngliche Archiv. Der ursprüngliche Autostart-/Shortcutname wird nicht überschrieben.

Baseline: 74 Kernprüfungen sowie programmgesteuerte WPF-Prüfungen bestanden. Aktuelle Ergebnisse stehen getrennt in CURRENT_STATE.md. Vollständige Funktionsparität und Releasefreigabe sind nicht aus dieser Basis ableitbar.
