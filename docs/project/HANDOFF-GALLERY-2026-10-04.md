# Galeriebedienung – 04.10.2026

Die Galerie bleibt innerhalb des Hauptfensters. Mausrad ohne Strg zoomt an der Zeigerposition zwischen eingepasster Ansicht und 600 %. Linke Maustaste auf dem vergrößerten Bild gedrückt halten und ziehen, um den Ausschnitt zu verschieben. Die Verschiebung bleibt an den Bildgrenzen begrenzt; Loslassen, Verlust des Mausfangs oder Schließen beendet das Ziehen.

Pfeile am linken/rechten Bildrand wechseln Bilder. Bei nur einem Bild sind sie ausgeblendet. Jeder Bildwechsel setzt Zoom und Ausschnitt zurück. „Einpassen“ setzt auf 100 %, Escape schließt. Der Zoomregler und die Bild-Scrollleisten wurden entfernt; die übrigen App-Seiten behalten ihr normales Scrollverhalten.

Geändert: Views/GalleryOverlay.cs und gezielte UiChecks. Die Galerie verwendet einen abgeschnittenen Bildbereich mit Skalierungs-/Verschiebungstransformation, statt einen ScrollViewer. Dadurch greift das allgemeine SmoothScroll-Verhalten nicht in den Mausrad-Zoom ein. Tastaturpfeile bleiben erhalten.

Prüfungen: WPF-Tests für tatsächliches Bilddekodieren/Layout, Mausrad ohne Strg, Verschiebung, Ende des Ziehens/Mausfang, Zoomgrenzen, beide seitlichen Buttons und Reset beim Bildwechsel. Dies sind programmgesteuerte Tests, keine behauptete manuelle Hardware-Mausabnahme. Endgültige Ergebnisse siehe CURRENT_STATE.md und docs/verification/gesture-*.
