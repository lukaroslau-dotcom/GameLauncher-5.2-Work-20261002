# Entwicklungsstand 04.10.2026 – Galerie mit direkter Mausbedienung

Aktuelles Delta auf b5f7c13: Mausradzoom ohne Strg an der Zeigerposition (1–6×), linke Maustaste zum Verschieben, begrenzte Bildränder, seitliche Pfeile, Reset bei Bildwechsel, kein Zoomslider/keine Bildscrollleisten. Galerie bleibt im Hauptfenster. Details HANDOFF-GALLERY-2026-10-04.md.

Aktuell bestanden: 77 programmgesteuerte WPF-UI-Checks, self-contained Publish und zwei Starts der veröffentlichten EXE mit je 14 Checks; Belege docs/verification/gesture-*. Bilddekodierung, Mausrad, Verschiebung, Mausfangende, Zoomgrenzen und beide Pfeile geprüft. Gerenderte Galerie visuell geprüft. Keine manuelle Hardware-Mausabnahme behauptet. Nach Tests keine Produktänderung; lediglich Whitespace/Dokumentation. Frühere Kern-/Prozessgates unten sind Nachweise des vorherigen funktionalen Stands, wurden für diese reine Galerieänderung nicht erneut ausgeführt.

# Entwicklungsstand 04.10.2026 – geprüfter Inline-Kandidat

Revision auf 695b11f, Commitbetreff `feat: integrate catalog and gallery with learned wishlist matching`. Separate WPF/.NET10-Kopie; Original und V7 unverändert.

Suche ist eine Seite mit Coverzeilen; ausgewählte Treffer werden in der Wunschliste gemerkt und samt Name/Version/Quelle/Cover/Metadaten ins Hinzufügenformular übernommen. Vorhandene lokale Eingaben bleiben erhalten. Galerie und neue Zuordnungsfragen sind Overlays im selben Hauptfenster. CatalogResult enthält Coveradresse/Version, sodass Vorschauen nur Cover laden und vollständige Metadaten erst beim Auswählen abgerufen werden.

Zwei größere intelligente Erweiterungen: erklärbare Zuordnung über Titel/Aliase/Abkürzung/Tippfehler und durchgehende Wunschlistenidentität mit bestätigtem Aliaslernen. Drei-Buchstaben-Aliase werden nach Bestätigung exakt erkannt. Bis zu fünf Vorschläge, unsichere Treffer bestätigen, Abbruch separat von neuem Eintrag. Funde markieren mögliche Zuordnungen und melden neue Vorschläge ohne Wiederholungsmeldung bei unverändertem Scan. Weitere kleine Hilfen und Bedienung in HANDOFF-2026-10-04.md.

Bestanden: 142 Kernchecks, sieben echte Prozesschecks, 69 WPF-UI-Checks, self-contained Windows-x64-Publish, zwei tatsächliche EXE-Starts mit jeweils 14 Checks. Neue Prüfungen umfassen Covercache/-übernahme, Formularpfade, Wunschlisteneintrag, verspätete Suchantworten, Inline-Galerie, begründete Auswahl/Abbruch, Abkürzungen, Tippfehler, Fortsetzungszahlen und persistente Aliase. Screenshots der Such-/Galerie-/Zuordnungsansicht zusätzlich visuell geprüft. 58 Originalquellen weiterhin unverändert. Rohbelege docs/verification/inline-*.

Grenzen: programmgesteuerte UI-Tests, keine vollständige manuelle Native-/Picker-/Mehrmonitorabnahme. Öffentlicher Katalog liefert im Liveprobe Cover und Version; Onlineendpunkte sind nicht garantiert stabil. Kein neuer Login. Algorithmische Zuordnung ist keine KI-Wahrscheinlichkeit. Alle älteren nicht erneut geprüften LAN/Autostart/Crash/Datenträger-/Großdownloadgrenzen bleiben in FEATURE_MATRIX.md. Keine pauschale Gesamtfreigabe. Andere geerbte Fenster außer Suche/Galerie wurden nicht vollständig ersetzt.
