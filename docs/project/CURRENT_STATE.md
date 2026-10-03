# Entwicklungsstand 2026-10-03: Smart-Catalog-Integrationskandidat

Revision auf b5ed80f; Commitbetreff `feat: add smart catalog matching and media workflows`. Separate WPF/.NET-10-Kopie, Original und V7 unverändert.

Umgesetzt: direkt erreichbare Wunschliste nur mit Namen; öffentlicher F95-Namenskatalog mit Offlinecache und verbundenem optionalen Account-Provider-Vertrag; gewählte Treffer merken; exakte Zuordnung neuer stabiler EXEs und fertig entpackter Archive ohne Verlust der ID/Metadaten. Entwickler, Tags, Beschreibung und Engine werden ergänzt, manuell markierte Felder geschützt. Galerie mit Zoom/Pfeilen/Maximieren und unabhängigem Schließen. Diagnose als eigenes Fenster. Animiertes Pixelscrollen mit Reduced Motion. Beide Update-Listen teilen einen durchgängigen Scrollbereich. Paketimport prüft Hashmanifest und erstellt eine neue Datenkopie.

Bestanden am finalen funktionalen Stand: 133 Kernchecks + sieben echte Prozesschecks; 59 WPF-UI-Checks; self-contained Publish; zwei echte EXE-Starts mit je 14 Checks einschließlich Diagnose-/Galerieschließen. Live-Namenssuche mit App-HTTP-Client: 23 Treffer ohne Login. 58 Originalquellen erneut unverändert. Rohbelege unter docs/verification/revision-*. Keine Produktänderung nach diesen Tests; Dokumentations-/Paketarbeiten separat.

Neue Kurzzeitmessung: settledIdle 0,52 % / 0 % eines Kerns; Spielmodus 8,29 % / 7,26 %. Nur drei Sekunden je Fenster, vier logische CPUs. Kein genereller Performancegewinn und keine Langzeitabnahme abgeleitet. Die zusätzliche Galerieprüfung verändert den Speicherverlauf gegenüber dem früheren Kandidaten.

Offen: regulärer Login-/2FA-Adapter und authentifizierter Linkresolver; native manuelle Desktopabnahme; große reale Download-/Watcher-/Crash-/Datenträgerfälle; LAN/Autostart-Liveprüfung; vollständige Originalparität. Frühere Anforderungen wie Publisherfeeds, persistenter kleiner Prozesshost und konfigurierbare Homereihen bleiben offen. Deshalb keine pauschale Release-/Gesamtfreigabe. Details FEATURE_MATRIX.md und HANDOFF-2026-10-03.md.
