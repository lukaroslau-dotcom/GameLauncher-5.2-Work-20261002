using System.IO;
using System.IO.Compression;
using System.Text.Json;
using GameLauncher.Constants;
using GameLauncher.models;
using GameLauncher.Service;

internal static class HardeningChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        string scope = Path.Combine(root, "Hardening");
        Directory.CreateDirectory(scope);
        var paths = new AppPaths(Path.Combine(scope, "Data"));
        var service = new GameService(paths);
        string save = Path.Combine(scope, "Save");
        string install = Path.Combine(scope, "Game");
        Directory.CreateDirectory(save); Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(save, "slot.dat"), "one");
        File.WriteAllText(Path.Combine(install, "game.exe"), "fixture");
        var game = new Game { Id = "safety", Name = "Safety", GamePath = install, ExecutablePath = Path.Combine(install, "game.exe"), SavePath = save, IsInstalled = true };
        service.AddGame(game); service.SaveGames();
        service.UpdateStatus(game.Id, GameStatus.Finished); service.SaveGames();
        var finished = new GameService(paths).ShowGame(game.Id)!;
        check(finished.Status == GameStatus.Finished && finished.IsInstalled && File.Exists(finished.ExecutablePath) && File.Exists(Path.Combine(save, "slot.dat")), "Finished preserves installation, saves and identity across restart");
        service.UpdateStatus(game.Id, GameStatus.WaitingForUpdates);
        check(game.IsInstalled && game.GamePath == install, "Waiting status does not uninstall a game");
        check(service.ArchiveSave(game) && VerifiedSaveBackup.Verify(paths.GetSaveArchiveForGame(game.Id)), "Backup is published with a verified SHA-256 manifest");
        File.WriteAllText(Path.Combine(save, "slot.dat"), "two"); check(service.ArchiveSave(game), "Second verified backup succeeds");
        File.WriteAllText(Path.Combine(save, "slot.dat"), "three"); check(service.ArchiveSave(game), "Third verified backup succeeds");
        check(Directory.GetDirectories(Path.Combine(paths.ArchivePath, "SaveHistory", game.Id)).Length == 1 && Directory.GetDirectories(Path.Combine(paths.ArchivePath, "RetiredSaves", game.Id)).Length == 1, "Two active backups; older backup retired without deletion");
        string target = Path.Combine(scope, "Restore"); Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "current.dat"), "current");
        check(service.RestoreSave(game.Id, target, SaveRestoreMode.Replace) && !File.Exists(Path.Combine(target, VerifiedSaveBackup.ManifestName)) && Directory.GetDirectories(scope, "Restore.previous-*").Length == 1, "Restore verifies payload, excludes manifest and preserves previous save");
        File.WriteAllText(Path.Combine(paths.GetSaveArchiveForGame(game.Id), "slot.dat"), "tampered");
        check(!service.RestoreSave(game.Id, target, SaveRestoreMode.Replace) && File.ReadAllText(Path.Combine(target, "slot.dat")) == "three", "Tampered backup cannot replace current saves");
        string legacy = "[{\"Id\":\"unknown\",\"Name\":\"Preserve\",\"FutureMetadata\":{\"version\":4}}]";
        string legacyPath = Path.Combine(scope, "legacy.json"); File.WriteAllText(legacyPath, legacy);
        check(new ImportService().ImportGamesJson(legacyPath, service).added == 1 && File.ReadAllText(paths.GamesFilePath).Contains("FutureMetadata"), "Unknown legacy game fields survive import and save");
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"FutureSetting\":17}")!;
        check(JsonSerializer.Serialize(settings).Contains("FutureSetting"), "Unknown settings survive serialization");
        foreach (string entryName in new[] { "../escape.txt", "C:/escape.txt", "file:stream", "CON.txt", "trail./file", "nested/../../escape" })
        {
            string zip = Path.Combine(scope, Guid.NewGuid() + ".zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) { using var writer = new StreamWriter(archive.CreateEntry(entryName).Open()); writer.Write("bad"); }
            var result = ArchiveExtractionService.Extract(zip, Path.Combine(scope, "Extract"), CancellationToken.None);
            check(!result.Success && File.Exists(zip), "Reject archive path: " + entryName);
        }
        string duplicate = Path.Combine(scope, "duplicate.zip");
        using (var archive = ZipFile.Open(duplicate, ZipArchiveMode.Create))
            foreach (string name in new[] { "game.txt", "GAME.txt" }) { using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write("data"); }
        check(!ArchiveExtractionService.Extract(duplicate, Path.Combine(scope, "Extract"), CancellationToken.None).Success, "Reject case-insensitive duplicate archive entries");
        string large = Path.Combine(scope, "large.zip");
        using (var archive = ZipFile.Open(large, ZipArchiveMode.Create)) { using var writer = new StreamWriter(archive.CreateEntry("data.bin").Open()); writer.Write(new string('a', 1024)); }
        check(!ArchiveExtractionService.Extract(large, Path.Combine(scope, "Extract"), CancellationToken.None, 100).Success && File.Exists(large), "Expansion budget enforced before publishing");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        check(!ArchiveExtractionService.Extract(large, Path.Combine(scope, "Extract"), cancellation.Token).Success && File.Exists(large), "Cancelled extraction preserves archive");

        var importer = new ImportService();
        string previewPath = Path.Combine(scope, "preview.json");
        File.WriteAllText(previewPath, "[{\"Name\":\"No ID\"}]");
        var preview = importer.Preview(previewPath, service);
        File.WriteAllText(previewPath, "[{\"Name\":\"Changed\"}]");
        check(importer.ImportGamesJson(previewPath, service, preview.Sha256).error != null, "Import rejects a source changed after preview");
        var first = importer.ImportGamesJson(previewPath, service);
        var second = importer.ImportGamesJson(previewPath, service);
        check(first.added == 1 && second.added == 0 && second.skipped == 1, "Repeated import without legacy IDs is idempotent");
        check(Directory.EnumerateFiles(Path.Combine(paths.ArchivePath, "ImportOriginals")).Any(), "Import preserves a byte-identical original before committing");

        var isolatedSettings = new SettingsService(paths);
        isolatedSettings.LoadAndResolvePaths(); isolatedSettings.Settings.BackupBeforePlay = false; isolatedSettings.Save(paths);
        isolatedSettings.Settings.BackupBeforePlay = true; isolatedSettings.Save(paths);
        check(File.Exists(paths.SettingsFilePath + ".bak") && new SettingsService(paths).LoadAndResolvePaths().ArchivePath == paths.ArchivePath, "Settings use atomic replacement and keep a recovery copy");
        File.WriteAllText(paths.SettingsFilePath, "corrupt settings");
        bool loadBlocked = false, saveBlocked = false;
        try { new SettingsService(paths).LoadAndResolvePaths(); } catch (IOException) { loadBlocked = true; }
        try { isolatedSettings.Save(paths); } catch (IOException) { saveBlocked = true; }
        check(loadBlocked && saveBlocked && File.ReadAllText(paths.SettingsFilePath) == "corrupt settings", "Corrupt settings cannot be silently reset or overwritten");
        string transferFile = Path.Combine(scope, "settings-export.json");
        var currentSettings = new AppSettings { ArchivePath = "original", StartWithWindows = false, RemoteHostPort = 17840 };
        SettingsTransfer.Export(new AppSettings { ArchivePath = "foreign", StartWithWindows = true, RemoteHostPort = 4567, ReduceMotion = true }, transferFile);
        var settingsPreview = SettingsTransfer.Inspect(transferFile, currentSettings);
        var importedSettings = SettingsTransfer.Apply(settingsPreview, currentSettings);
        check(importedSettings.ArchivePath == "original" && !importedSettings.StartWithWindows && importedSettings.RemoteHostPort == 17840 && importedSettings.ReduceMotion, "Settings import preserves machine paths, autostart and network authority");
        bool stale = false, overwrite = false, changed = false;
        currentSettings.ReduceTransparency = true;
        try { SettingsTransfer.Apply(settingsPreview, currentSettings); } catch (IOException) { stale = true; }
        try { SettingsTransfer.Export(currentSettings, transferFile); } catch (IOException) { overwrite = true; }
        currentSettings.ReduceTransparency = false; File.AppendAllText(transferFile, " ");
        try { SettingsTransfer.Apply(settingsPreview, currentSettings); } catch (IOException) { changed = true; }
        check(stale && overwrite && changed, "Settings reject stale preview, existing export and tampered source");

        string package = Path.Combine(scope, "library.zip");
        LibraryPackage.Export(service, package);
        using (var zip = ZipFile.OpenRead(package))
            check(zip.GetEntry("export-manifest.json") != null && zip.Entries.Any(e => e.FullName.StartsWith("SaveArchive/")) && !zip.Entries.Any(e => e.FullName.StartsWith("Downloads/")), "Verified library package contains manifest and save backups, excludes downloads");
        bool packageExisting = false, packageCancelled = false;
        try { LibraryPackage.Export(service, package); } catch (IOException) { packageExisting = true; }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        string cancelledPackage = Path.Combine(scope, "cancelled.zip");
        try { LibraryPackage.Export(service, cancelledPackage, cancelled.Token); } catch (OperationCanceledException) { packageCancelled = true; }
        check(packageExisting && packageCancelled && !File.Exists(cancelledPackage) && !Directory.EnumerateFiles(scope, "*.pending-*").Any(), "Package export rejects overwrite and cancels without publishing partial files");
        game.Aliases.Add("GS"); game.Tags.Add("Adventure"); game.LaunchArguments = "--windowed";
        check(LocalGameSearch.Matches(game, "Saftey") && LocalGameSearch.Matches(game, "GS") && LocalGameSearch.Matches(game, "Adventure"), "Local search supports spelling mistakes, aliases and tags");
        string replacement = Path.Combine(scope, "Replacement"); Directory.CreateDirectory(replacement); File.WriteAllText(Path.Combine(replacement, "game.exe"), "new fixture");
        service.ApplyReinstall(game, replacement, Path.Combine(replacement, "game.exe"), save); service.SaveGames();
        var updated = new GameService(paths).ShowGame(game.Id)!;
        check(updated.Id == game.Id && updated.InstallationHistory.Single().GamePath == install && updated.LaunchArguments == "--windowed", "Updating preserves identity and a persisted previous-installation pointer");
        check(service.TryDeleteInstallation(game, out _), "Removal moves installation into a recovery directory");
        var receipt = InstallationRecovery.List(paths).Single();
        check(!Directory.Exists(replacement) && Directory.Exists(receipt.Stored), "Removed files remain available for recovery");
        Directory.CreateDirectory(replacement); File.WriteAllText(Path.Combine(replacement, "occupied"), "keep");
        bool occupied = false;
        try { InstallationRecovery.Restore(paths, receipt.Id); } catch (IOException) { occupied = true; }
        check(occupied && File.ReadAllText(Path.Combine(replacement, "occupied")) == "keep", "Recovery never overwrites an occupied destination");
        File.Delete(Path.Combine(replacement, "occupied")); Directory.Delete(replacement);
        File.WriteAllText(Path.Combine(receipt.Stored, "game.exe"), "tampered");
        bool tampered = false;
        try { InstallationRecovery.Restore(paths, receipt.Id); } catch (IOException) { tampered = true; }
        check(tampered && !Directory.Exists(replacement), "Recovery rejects changed installation bytes");
        File.WriteAllText(Path.Combine(receipt.Stored, "game.exe"), "new fixture");
        InstallationRecovery.Restore(paths, receipt.Id);
        check(File.Exists(Path.Combine(replacement, "game.exe")) && InstallationRecovery.List(paths).Count == 0, "Verified installation recovery restores original location");
    }
}
