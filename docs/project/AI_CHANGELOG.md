# AI changelog

## 2026-10-02 — Locate and preserve 5.2 baseline
- Model/reasoning: current Codex session; exact model routing not independently verified.
- Commit: none; independent filesystem copy, no product edits.
- Changed files: copied 58 source/project files, SOURCE-MANIFEST.json, docs/project/CURRENT_STATE.md, docs/project/AI_CHANGELOG.md.
- Result: actual 5.2.0 WPF source located and copied with matching SHA-256 hashes.
- Exact verification: dotnet build GameLauncher/GameLauncher.csproj -c Release; exit 0, 4 warnings, 0 errors. No runtime or regression test claim.
- Blockers: WPF preservation conflicts with supplied WinUI 3/React architecture rule; clarification pending.
- Baseline impact: establishes local source availability, not functionality or release readiness. Existing V7 repository unchanged.

## 2026-10-02 — 5.2 preservation and first reliability delta
- Model/reasoning: current Codex session; no delegated model or Astra review claimed.
- Commit: uncommitted delta on baseline 6e9d17d.
- Changed files: archive/path/backup services, GameService, GameLaunchService, AppPaths/SettingsService, models, card layout/context-menu, MainWindow status/import flows, HardeningChecks/Program, AGENTS.md and project records.
- Result: original archive retention, bounded managed extraction, verified backup/restore, retired history, independent status, preserved unknown JSON fields, five-card maximum. Default runtime data isolated from original archive.
- Tests: baseline 74 checks and WPF UI pass; intermediate compile failure corrected; 93 checks pass before latest data-root/settings delta. No release claim.
- Blockers/remaining: backup/download UI and automation, final regression/real starts/performance, full target matrix. Native interactive automation unavailable; programmatic WPF checks available.
- Baseline impact: 5.2 WPF is the selected product basis for this independent copy, per owner's decision authorization.

## 2026-10-03 — Integrationskandidat mit verifizierten Dateiflüssen und Windows-Nachweisen
- Model/reasoning: aktuelle Codex-Sitzung, keine Delegation/kein behauptetes Astra-Review; Laufzeit-Modellkennung nicht unabhängig ermittelt.
- Commit: dieser Eintrag gehört zum Commit mit Betreff `feat: integrate safe 5.2 workflows and verified Windows candidate`, auf Basissnapshot 6e9d17d; Hash über Git auflösbar.
- Geänderte Dateien: MainWindow-Teile/Views/Theme, Archiv/Backup/Recovery/Download/Import/Settings/Prozess-/Pfaddienste, additive Modelle, Tests/MigrationChecks/Smoke, scripts/Test-Published.ps1, README und docs/project plus docs/verification. Vollständige Dateiliste im Commit.
- Ergebnis: verbundene neue Seiten und sichere Dateiflüsse; ursprüngliche 5.2-Funktionen bleiben im Code; fünf größere Landscape-Cover. Neue Abbruchwege für Export/Entpacken. Keine pauschale Paritäts-/Releasebehauptung.
- Exakte Tests: `dotnet run --project GameLauncher.Tests -c Release -- --process-test <TestGame.exe>` 120 Kernchecks + 7 Prozesschecks; UI-Kommando in TESTING.md 48 Checks; `--migration-check <original games.json>` arbeitet auf Tempkopie, 42 übernommen/0 Wiederimport/Original unverändert; self-contained Publish; `./scripts/Test-Published.ps1` zweimal echte EXE mit Exit 0. Rohbelege versioniert in docs/verification.
- Blocker/Rest: Details FEATURE_MATRIX.md; native interaktive Desktopabnahme nicht verfügbar, Idle-CPU schwankt deutlich, vollständige Parität sowie Provider-/Publisher-/Paketimport-/Crashgrenzen offen.
- Baselineauswirkung: WPF/.NET10/JSON bleiben unverändert die Architektur; bestätigt sind additive Datenfelder, isolierter Standarddatenordner, verifizierte Dateiveröffentlichung und die tatsächlich ausgeführten Tests. 58 Originalquelldateien weiter identisch.

## 2026-10-03 — Wunschliste, Katalog und Medien: geprüfter Revisionskandidat
- Modell/Reasoning: aktuelle Codex-Sitzung; genaue Laufzeitkennung nicht unabhängig ermittelt; keine Delegation oder separates Architekturreview behauptet.
- Commit: dieser Eintrag gehört zum Commit `feat: add smart catalog matching and media workflows`, Elternstand b5ed80f; Hash über Git auflösbar.
- Dateien: CatalogSearchService/CatalogProviders/SmartAssociation/MetadataEnrichment, GalleryWindow/SmoothScroll/MainWindow.Media/Catalog, F95UpdateService, Modelle, XAML/Navigation, LibraryPackageImport, Tests, README und Projekt-/Verifikationsdokumente. Exakte Liste im Commit.
- Ergebnis: alle in HANDOFF-2026-10-03.md beschriebenen Ergänzungen verbunden. Nur exakte bewusst gemerkte uninstallierte Titel automatisch verknüpft; kein automatischer Updatezeigerwechsel. Accountadapter ist eine verbundene Schnittstelle, kein funktionierender Login. Paketimport in neue Kopie.
- Exakte Tests: `dotnet run --project GameLauncher.Tests -c Release -- --process-test <TestGame.exe>` 133 Kernchecks + sieben Prozesschecks; `--ui-test --data-root <Temp> --fixture-covers <lokale Kopien>` 59 Checks; `--catalog-online-check <Suchname>` echter Client 23 Treffer; `dotnet publish GameLauncher/GameLauncher.csproj -c Release -r win-x64 --self-contained true -o artifacts/GameLauncher-5.2-smart-candidate`; `./scripts/Test-Published.ps1 -Package ./artifacts/GameLauncher-5.2-smart-candidate` zwei EXE-Läufe mit je 14 Checks; 58 Originalhashes identisch. Zwischenzeitlicher Theme-URI-Fehler behoben und sämtliche betroffenen Gates erneut bestanden.
- Blocker/Grenzen: native manuelle Desktopsteuerung nicht verfügbar; kein autorisierter Accountadapter; weitere ältere Abnahmegrenzen in FEATURE_MATRIX.md. Kein Gesamtabschluss/Release behauptet.
- Baselineauswirkung: Architektur unverändert WPF/.NET10/JSON; additive Metadatenfelder, öffentlicher Suchprovider und Paketimport bestätigt. Öffentlicher Endpunkt/Metadatencache sind externe Abhängigkeiten.

## 2026-10-04 — Integrierte Suche/Galerie und lernende Zuordnung
- Modell/Reasoning: aktuelle Codex-Sitzung, keine Delegation; genaue Modellkennung nicht unabhängig ermittelt.
- Commit: dieser Eintrag gehört zum Commit `feat: integrate catalog and gallery with learned wishlist matching`, Elternstand 695b11f, Hash über Git auflösbar.
- Dateien: MainWindow.Catalog/Media/Association/Runtime/Smoke/XAML, CatalogCard/CatalogSearchService/SmartAssociation/ImportService, GalleryOverlay statt GalleryWindow, CatalogChecks/UiChecks, README und Projektdokumente/Verifikationsbelege. Exakte Liste im Commit.
- Ergebnis: Suche/Galerie ohne eigenes Fenster; Wunschlistenübernahme ins Formular; begründete Abkürzungs-/Tippfehlervorschläge; bestätigte Aliase; ID-erhaltende Wunschinstallation; Covercache, Generation/Cancellation, Pfaderhalt, Duplikatvermeidung und deduplizierte Scanmeldungen.
- Exakte Tests: `dotnet run --project GameLauncher.Tests -c Release -- --process-test <TestGame.exe>` 142 Kernchecks + sieben Prozesschecks; `dotnet run --project GameLauncher.Tests -c Release -- --ui-test --data-root <Temp> --fixture-covers <evidence/ui-covers>` 69 Checks; `dotnet publish GameLauncher/GameLauncher.csproj -c Release -r win-x64 --self-contained true -o artifacts/GameLauncher-5.2-inline-candidate`; `./scripts/Test-Published.ps1 -Package ./artifacts/GameLauncher-5.2-inline-candidate` zwei echte EXE-Läufe mit je 14 Checks. Livekatalog-Shapeprobe bestätigt cover/version-Felder. 58 Originalhashes identisch. Finale UI/Publish/EXE-Gates nach letztem Aliasfix wiederholt; keine Produktänderung danach.
- Behobener Zwischenfehler: WPF/WinForms-Typmehrdeutigkeit im neuen Overlay; anschließend Build/UI/Regression bestanden.
- Blocker/Grenzen: keine vollständige manuelle/native/LAN/Crash-/große Downloadabnahme, keine neue Anmeldung; ältere Grenzen unverändert. Keine pauschale Releasefreigabe.
- Baseline: WPF/.NET10/JSON unverändert; additive CatalogResult-Cover/Versionsfelder; bestätigte neue Laufzeitabläufe laut Belegen.

## 2026-10-04 — Direkte Galeriebedienung
- Modell/Reasoning: aktuelle Codex-Sitzung, keine Delegation, genaue Laufzeitkennung nicht unabhängig ermittelt.
- Commit: dieser Eintrag gehört zu `feat: add pointer zoom and drag pan to inline gallery`, Elternstand b5f7c13.
- Dateien: Views/GalleryOverlay.cs, GameLauncher.Tests/UiChecks.cs, README, CURRENT_STATE, TESTING, HANDOFF-GALLERY und gesture-Verifikationsbelege.
- Ergebnis: Zoom per Mausrad ohne Strg, zeigerbezogener Zoomanker, Drag/Pan mit linker Maustaste und Begrenzung, seitliche Bildpfeile, Reset beim Bildwechsel. Keine Änderung außerhalb der Galeriebedienung am Produkt.
- Tests: `dotnet run --project GameLauncher.Tests -c Release -- --ui-test --data-root <Temp> --fixture-covers <evidence/ui-covers>` 77 Checks, Exit 0; `dotnet publish GameLauncher/GameLauncher.csproj -c Release -r win-x64 --self-contained true -o artifacts/GameLauncher-5.2-gesture-candidate` Exit 0; `./scripts/Test-Published.ps1 -Package ./artifacts/GameLauncher-5.2-gesture-candidate` zwei echte EXE-Starts mit je 14 Checks, Exit 0. Gerendertes Bild zusätzlich visuell geprüft. Erstes Build mit mehrdeutigen WPF/WinForms-Namen korrigiert. Keine unbelegten manuellen Mausaktionen behauptet.
- Blocker/Grenzen: keine native manuelle Hardware-Maus-/Mehrmonitorabnahme; bestehende externe Grenzen unverändert. Kern-/Prozesschecks des Vorgängers nicht erneut als aktueller Lauf ausgegeben.
- Baseline: keine Architektur-/Datenformatänderung.
