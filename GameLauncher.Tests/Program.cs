using System.IO;
using System.IO.Compression;
using GameLauncher.Constants;
using GameLauncher.models;
using GameLauncher.Service;
using System.Text.Json;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Diagnostics;

if (args.Length == 2 && args[0] == "--migration-check") { MigrationChecks.Run(args[1]); return; }
if (args.Contains("--ui-test")) { try { UiChecks.Run(); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; } return; }
if(args.Length==2&&args[0]=="--gif-check")
{
    var animation=await ImageCache.LoadGif(args[1],420)??throw new Exception("GIF could not be decoded");var hashes=new HashSet<string>();
    foreach(var frame in animation.Frames){int stride=(frame.PixelWidth*frame.Format.BitsPerPixel+7)/8;var pixels=new byte[stride*frame.PixelHeight];frame.CopyPixels(pixels,stride,0);hashes.Add(Convert.ToHexString(SHA256.HashData(pixels)));}
    Console.WriteLine($"GIF frames={animation.Frames.Count}, unique={hashes.Count}, firstDelay={animation.Delays[0].TotalMilliseconds}ms");return;
}
if(args.Contains("--stream-check"))
{
    var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();int port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();
    using var host=new RemoteHostService();host.Start(port,60,75,1280);
    using var socket=new ClientWebSocket();await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/control?pin={host.Pin}"),CancellationToken.None);
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));byte[] buffer=new byte[1024*1024];long bytes=0;var watch=Stopwatch.StartNew();
    try
    {
        for(int frame=0;frame<30;frame++)
        {
            WebSocketReceiveResult result;do{result=await socket.ReceiveAsync(buffer,timeout.Token);bytes+=result.Count;}while(!result.EndOfMessage);
            byte[] ack=System.Text.Encoding.UTF8.GetBytes("{\"type\":\"frameAck\"}");await socket.SendAsync(ack,WebSocketMessageType.Text,true,timeout.Token);
        }
        watch.Stop();Console.WriteLine($"Stream frames=30, fps={30/watch.Elapsed.TotalSeconds:0.0}, avg={bytes/30/1024:0} KiB, capture={host.CaptureTechnology}, producer={host.ActualFps} FPS");
    }
    catch(OperationCanceledException){Console.WriteLine($"Stream diagnostic unavailable: {host.LastError}");Environment.ExitCode=2;}
    return;
}

string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "GameLauncher.Tests", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
int passed = 0;
void Check(bool condition, string title)
{
    if (!condition) throw new Exception("FAIL: " + title);
    Console.WriteLine("PASS: " + title); passed++;
}
string Folder(string name) { string path = Path.Combine(root, name); Directory.CreateDirectory(path); return path; }
string sizeFolder = Folder("InstalledSize");
Check(await GameSizeService.CalculateAsync(sizeFolder) == 0, "Empty installation has zero bytes");
File.WriteAllBytes(Path.Combine(sizeFolder, "game.bin"), new byte[1024]);
string sizeNested = Folder("InstalledSize/data/nested");
File.WriteAllBytes(Path.Combine(sizeNested, "assets.bin"), new byte[2048]);
Check(await GameSizeService.CalculateAsync(sizeFolder) == 3072, "Installation size includes nested files");
Check(GameSizeService.Format(3072) == "3.0 KB" && GameSizeService.Format(0) == "0 B" &&
    GameSizeService.Format(5_153_960_755L) == "4.8 GB", "Installation size formats zero, KB and sizes above 4 GB");
Check(await GameSizeService.CalculateAsync(Path.Combine(root, "MissingInstallation")) == null &&
    await GameSizeService.CalculateAsync("") == null && await GameSizeService.CalculateAsync("\0") == null &&
    await GameSizeService.CalculateAsync(Path.Combine(sizeFolder, "game.bin")) == null,
    "Missing, invalid and non-directory installation paths are handled safely");
Check(GameSizeService.Format(null) == "Größe unbekannt", "Unknown size never claims zero bytes");
File.WriteAllBytes(Path.Combine(sizeNested, "assets.bin"), new byte[4096]);
Check(await GameSizeService.CalculateAsync(sizeFolder) == 5120, "A new scan reflects changed installation contents");
using (var sizeCancellation = new CancellationTokenSource())
{
    sizeCancellation.Cancel();
    bool sizeCancelled = false;
    try { await GameSizeService.CalculateAsync(sizeFolder, sizeCancellation.Token); }
    catch (OperationCanceledException) { sizeCancelled = true; }
    Check(sizeCancelled, "Installation scans support cancellation");
}
Check(!JsonSerializer.Serialize(new Game()).Contains("InstalledSize", StringComparison.Ordinal),
    "Installation size does not change persisted game data");
var paths = new AppPaths(Path.Combine(root, "Archive"));
var service = new GameService(paths);
var game = new Game
{
    Id = "stable-test-game", Name = "Test game", GameVersion = "1.0", InstallSource = "Local",
    GamePath = Folder("Game"), SavePath = Folder("Saves"), Status = "installed"
};
game.ExecutablePath = Path.Combine(game.GamePath, "test.exe");
File.WriteAllText(game.ExecutablePath, "fixture, never executed");
File.WriteAllText(Path.Combine(game.SavePath, "slot.dat"), "original save");
Directory.CreateDirectory(Path.Combine(game.SavePath, "nested"));
File.WriteAllText(Path.Combine(game.SavePath, "nested", "world.dat"), "world");
service.AddGame(game);
game.Playtime = TimeSpan.FromMinutes(18);
service.SaveGames();
var loaded = new GameService(paths);
Check(loaded.GetInstalledGames().Single().Id == game.Id && loaded.GetGames()[0].Playtime == game.Playtime, "Persist/reload preserves ID and playtime");
Check(service.QueryGames(GameListFilter.All, "LOCAL").Count == 1, "Search source case-insensitively");
Check(F95UpdateService.TryExtractThreadId("https://f95zone.to/threads/example-game.230209/", out string threadId) && threadId == "230209",
    "Extract stable F95 thread ID from source URL");
Check(!F95UpdateService.TryExtractThreadId("https://f95zone.to.evil.example/threads/game.230209/", out _),
    "Reject lookalike F95 hosts");
Check(F95UpdateService.TryExtractThreadId("https://f95zone.com/threads/game.230209/", out _) &&
    F95UpdateService.TryExtractThreadId("https://www.f95zone.ninja/threads/game.230209/", out _) &&
    !F95UpdateService.TryExtractThreadId("https://f95zone.com.evil.example/threads/game.230209/", out _),
    "Accept known F95 mirrors without accepting spoofed hosts");
Check(F95UpdateService.NormalizeVersion(" [ v0.4.1 ] ") == F95UpdateService.NormalizeVersion("Version: v0.4.1") &&
    F95UpdateService.NormalizeVersion("[Version v0.4.1]") == "0.4.1" &&
    F95UpdateService.NormalizeVersion("[18+] My Game [v0.4.1]") == "0.4.1" &&
    F95UpdateService.NormalizeVersion("\r\n") == null && F95UpdateService.NormalizeVersion("\u200B\uFEFF") == null &&
    !F95UpdateService.VersionsDiffer("[v0.4.1]", "v 0.4.1") &&
    !F95UpdateService.VersionsDiffer("   ", "v0.4.1"),
    "Normalize bracketed versions and never flag blank installed versions");
string remoteStamp = "99", remoteVersion = "[v0.4.1]";
var f95Handler = new StubHttpMessageHandler(request =>
{
    string body = request.RequestUri!.AbsolutePath.Contains("/fast", StringComparison.Ordinal)
        ? "{\"230209\":" + remoteStamp + "}"
        : "{\"name\":\"Example Game\",\"version\":\"" + remoteVersion + "\",\"developer\":\"Studio\",\"status\":\"1\",\"image_url\":\"https://attachments.f95zone.to/cover.jpg\",\"previews_urls\":\"[\\\"https://attachments.f95zone.to/one.png\\\",\\\"https://attachments.f95zone.to/two.jpg\\\"]\"}";
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
});
using var f95Client = new HttpClient(f95Handler) { BaseAddress = new Uri("https://api.f95checker.dev/") };
using var f95 = new F95UpdateService(f95Client);
F95Metadata f95Metadata = await f95.FetchMetadataAsync("https://f95zone.to/threads/example-game.230209/", CancellationToken.None);
Check(f95Metadata is { Name: "Example Game", Version: "[v0.4.1]", Developer: "Studio", Status: "Laufend" } && f95Metadata.PreviewUrls.Count==2,
    "Read safe F95 metadata and gallery URLs through fast/full protocol");
var f95Game = new Game { Id = "f95-test", Name = "Example Game", GameVersion = "v0.4.0",
    InstallSource = "https://f95zone.to/threads/example-game.230209/", Status = GameStatus.Installed, IsInstalled = true };
F95UpdateReport updateReport = await f95.CheckAsync(new[] { f95Game }, CancellationToken.None);
Check(updateReport.Updates == 1 && f95Game.UpdateAvailable && f95Game.LatestKnownVersion == "[v0.4.1]" && f95Game.F95ThreadId == "230209",
    "Detect and retain a newer F95 version");
remoteStamp = "100"; remoteVersion = "v0.4.2";
F95UpdateReport nextRelease = await f95.CheckAsync(new[] { f95Game }, CancellationToken.None);
Check(nextRelease.NewUpdates.Count == 1 && f95Game.LatestKnownVersion == "v0.4.2",
    "Notify again when a newer online release appears while an older update is pending");
f95Game.GameVersion = "Version 0.4.2";
await f95.CheckAsync(new[] { f95Game }, CancellationToken.None);
Check(!f95Game.UpdateAvailable, "Clear update after installed version matches normalized online version");
f95Game.GameVersion = "0.4.1";
f95Game.LatestKnownVersion = "v0.4.2";
f95Game.IgnoredUpdateVersion = "[v0.4.2]";
Check(!F95UpdateService.IsUpdateAvailable(f95Game.GameVersion, f95Game.LatestKnownVersion, f95Game.IgnoredUpdateVersion),
    "Ignore only the selected normalized online version");
f95Game.LatestKnownVersion = "v0.4.3";
Check(F95UpdateService.IsUpdateAvailable(f95Game.GameVersion, f95Game.LatestKnownVersion, f95Game.IgnoredUpdateVersion),
    "Show the next online version after an ignored release");
f95Game.Notes = "Eine geheime Akademie";
f95Game.F95Developer = "HappySteveGames";
f95Game.ModUrl = "https://f95zone.to/threads/example-mod.123/";
var extendedSearch = new GameService(new AppPaths(Folder("ExtendedSearch")));
extendedSearch.AddGame(f95Game);
Check(new[] { "Akademie", "happystevegames" }.All(term => extendedSearch.QueryGames(GameListFilter.All, term).Count == 1),
    "Search includes notes and F95 developer");
var f95Paths = new AppPaths(Path.Combine(root, "F95Persistence"));
var f95Persistence = new GameService(f95Paths);
f95Persistence.AddGame(f95Game); f95Persistence.SaveGames();
var persistedF95 = new GameService(f95Paths).ShowGame("f95-test")!;
Check(persistedF95.LatestKnownVersion == "v0.4.3" && persistedF95.IgnoredUpdateVersion == "[v0.4.2]" && persistedF95.LastUpdateCheckUtc.HasValue && persistedF95.ModUrl==f95Game.ModUrl,
    "Persist F95 update and mod metadata across restart");
f95Game.UpdateAvailable=true;
Check(extendedSearch.QueryGames(GameListFilter.UpdatesAvailable,null).Single().Id==f95Game.Id &&
    extendedSearch.QueryGames(GameListFilter.NeverPlayed,null).Single().Id==f95Game.Id,
    "Useful library filters expose updates and unplayed games");
string previewFolder = Path.Combine(f95Paths.GamesDataPath, "F95Preview");
Directory.CreateDirectory(previewFolder);
string previewCover = Path.Combine(previewFolder, "230209.jpg");
File.WriteAllText(previewCover, "image fixture");
string retainedCover = f95Persistence.CacheCover(previewCover, f95Game.Id);
Check(File.Exists(retainedCover) && !File.Exists(previewCover),
    "Move F95 preview covers into the permanent cache without leaving duplicate files");

var recoveryPaths = new AppPaths(Path.Combine(root, "Recovery"));
var recoveryWriter = new GameService(recoveryPaths);
var recoveryGame = new Game { Id = "recovery", Name = "Sicherer Stand", GameVersion = "1", InstallSource = "Lokal" };
recoveryWriter.AddGame(recoveryGame); recoveryWriter.SaveGames();
recoveryGame.Name = "Neuer Stand"; recoveryWriter.SaveGames();
File.WriteAllText(recoveryPaths.GamesFilePath, "{ beschädigt");
var recoveredLibrary = new GameService(recoveryPaths);
Check(recoveredLibrary.ShowGame("recovery")?.Name == "Sicherer Stand" &&
    recoveredLibrary.LoadError?.Contains("wiederhergestellt", StringComparison.OrdinalIgnoreCase) == true,
    "Recover a corrupt library automatically from the last safe copy");
recoveredLibrary.SaveGames();
Check(JsonDocument.Parse(File.ReadAllText(recoveryPaths.GamesFilePath)).RootElement.ValueKind == JsonValueKind.Array,
    "Recovered library remains writable and valid");
File.WriteAllText(recoveryPaths.GamesFilePath, "extern beschädigt");
recoveredLibrary.SaveGames();
Check(JsonDocument.Parse(File.ReadAllText(recoveryPaths.GamesFilePath)).RootElement.ValueKind == JsonValueKind.Array,
    "Rewrite externally changed library data even when the in-memory model is unchanged");
var missingPathGame = new Game
{
    Id = "missing-path", Name = "Verschoben", Status = GameStatus.Installed, IsInstalled = true,
    GamePath = Path.Combine(root, "nicht-mehr-da"), ExecutablePath = Path.Combine(root, "nicht-mehr-da", "game.exe")
};
missingPathGame.PathWarning = GameService.GetPathWarning(missingPathGame);
var pathCheckService = new GameService(new AppPaths(Folder("PathChecks")));
pathCheckService.AddGame(missingPathGame);
Check(missingPathGame.PathWarning == "Spielordner fehlt" &&
    pathCheckService.QueryGames(GameListFilter.NeedsAttention, null).Single().Id == "missing-path",
    "Detect missing installations and expose them through the attention filter");
Check(service.ArchiveSave(game), "Archive nested saves");
Check(service.ArchiveSave(game) && Directory.GetDirectories(Path.Combine(paths.ArchivePath, "SaveHistory", game.Id)).Length == 1,
    "Archive replacement retains previous save version");
Check(File.ReadAllText(Path.Combine(paths.GetSaveArchiveForGame(game.Id), "nested", "world.dat")) == "world", "Archive preserves directory tree");
string validSave = game.SavePath;
game.SavePath = Path.Combine(root, "MissingSave");
Check(!service.ArchiveSave(game) && Directory.Exists(game.GamePath), "Missing save does not permit archive success");
game.SavePath = game.GamePath;
Check(!service.ArchiveSave(game), "Reject save equal to game folder");
game.SavePath = validSave;
Check(!service.RestoreSave(game.Id, paths.SaveArchivePath, SaveRestoreMode.Replace), "Reject restore inside archive");
Check(!service.RestoreSave(game.Id, game.GamePath, SaveRestoreMode.Replace), "Reject replacing installation with save data");
string target = Folder("Restore");
File.WriteAllText(Path.Combine(target, "new.dat"), "new save");
Check(!service.RestoreSave(game.Id, target, SaveRestoreMode.Cancel) && File.Exists(Path.Combine(target, "new.dat")), "Cancel preserves destination");
Check(service.RestoreSave(game.Id, target, SaveRestoreMode.Merge), "Merge restores archive");
Check(File.Exists(Path.Combine(target, "slot.dat")) && File.Exists(Path.Combine(target, "new.dat")), "Merge retains other files");
Check(service.RestoreSave(game.Id, target, SaveRestoreMode.Replace), "Replace restores archive");
Check(!File.Exists(Path.Combine(target, "new.dat")) && Directory.GetDirectories(root, "Restore.previous-*").Length == 2, "Replace keeps old destination backups");
Check(service.TryDeleteInstallation(game, out _), "Remove fixture installation");
service.UpdateStatus(game.Id, GameStatus.WaitingForUpdates);
service.SaveGames();
Check(service.GetInstalledGames().Count == 0 && new GameService(paths).GetWaitingForUpdatesGames().Count == 1, "Waiting lifecycle survives restart");
string reinstall = Folder("Reinstall");
File.WriteAllText(Path.Combine(reinstall, "test.exe"), "fixture");
service.ApplyReinstall(game, reinstall, Path.Combine(reinstall, "test.exe"), target);
Check(game.Id == "stable-test-game" && game.Playtime == TimeSpan.FromMinutes(18) && service.GetInstalledGames().Count == 1, "Reinstall preserves identity and history");
var other = new Game { Id = "other", Name = "Other game", GamePath = Folder("Reinstall/Other"), ExecutablePath = Path.Combine(reinstall, "Other", "other.exe") };
service.AddGame(other);
Check(!service.TryDeleteInstallation(game, out _) && Directory.Exists(reinstall), "Reject installation containing another game");
service.RemoveGame(other);
string import = Path.Combine(root, "import.json");
File.WriteAllText(import, """[{"Id":"safe","Name":"Safe"},{"Id":"../bad","Name":"Bad"}]""");
Check(new ImportService().ImportGamesJson(import, service).error != null && service.GetGames().Count == 1, "Invalid import is atomic");
File.WriteAllText(import, JsonSerializer.Serialize(new[] { game }));
Check(new ImportService().ImportGamesJson(import, service).skipped == 1, "Import skips duplicate IDs");
bool rejected = false;
try { paths.GetSaveArchiveForGame("../outside"); } catch (ArgumentException) { rejected = true; }
Check(rejected, "Reject archive ID traversal");
Check(!paths.CanSafelyDeleteGamePath(paths.ArchivePath), "Protect archive from installation deletion");
Check(!service.TryCopyDataTo(new AppPaths(Path.Combine(paths.ArchivePath, "nested")), out _), "Reject nested migration");
var migratedPaths = new AppPaths(Path.Combine(root, "Migrated"));
Check(service.TryCopyDataTo(migratedPaths, out _), "Migrate archive to empty target");
Check(new GameService(migratedPaths).GetGames().Single().Id == game.Id &&
    Directory.Exists(paths.GetSaveArchiveForGame(game.Id)), "Migration preserves history and original saves");
Check(!service.TryCopyDataTo(migratedPaths, out _), "Do not overwrite existing archive");
Check(service.TryDeleteArchivedSave(game.Id, out _) && !Directory.Exists(Path.Combine(paths.ArchivePath, "SaveHistory", game.Id)),
    "Absolute archive deletion removes version history");
var corruptPaths = new AppPaths(Path.Combine(root, "Corrupt"));
corruptPaths.EnsureCreated();
File.WriteAllText(corruptPaths.GamesFilePath, "{ invalid");
var corrupt = new GameService(corruptPaths);
bool blocked = false;
try { corrupt.SaveGames(); } catch (IOException) { blocked = true; }
Check(blocked && File.ReadAllText(corruptPaths.GamesFilePath) == "{ invalid", "Corrupt library cannot be overwritten");
string scan = Folder("Discovery");
string scanGame = Folder("Discovery/Adventure");
File.WriteAllText(Path.Combine(scanGame, "Adventure.exe"), "");
File.WriteAllText(Path.Combine(scanGame, "setup.exe"), "");
Directory.CreateDirectory(Path.Combine(scanGame, "renpy"));
Directory.CreateDirectory(Path.Combine(scanGame, "game", "saves"));
var detected = DiscoveryService.ScanFolder(scan, CancellationToken.None);
Check(detected.Count == 1 && detected[0].Confidence >= 90 && detected[0].SavePath.EndsWith("saves"), "Detect game engine and save folder, skip installers");
string locatedGame = Folder("SaveFinder/Game");
string locatedSave = Folder("SaveFinder/Game/saves");
Check(SaveLocationService.Find(locatedGame,"Unrelated title",CancellationToken.None).Any(x => PathSafety.Same(x.Path,locatedSave)),
    "Find save folder inside installation even when no external search root is available");
Check(PathSafety.Same(SaveLocationService.FindBest(locatedGame,"Unrelated title",CancellationToken.None)!.Path,locatedSave) &&
    PathSafety.Same(SaveLocationService.SuggestedBrowseFolder(locatedGame),locatedGame),
    "Choose a deterministic save automatically and browse from the game folder");
Check(LauncherShortcutService.NormalizeKey(" q ") == "Q", "Normalize launcher start shortcut key");
bool badLaunchKey = false;
try { LauncherShortcutService.NormalizeKey("F2"); } catch (ArgumentException) { badLaunchKey = true; }
Check(badLaunchKey, "Reject unsupported launcher start shortcut key");
var deletedGame = new Game { Id="deleted-game", Name="Deleted game", Status=GameStatus.Installed, IsInstalled=true };
service.AddGame(deletedGame);
service.ClearLocalPathsKeepHistory(deletedGame);
service.SaveGames();
var deletedReloaded = new GameService(paths);
Check(deletedGame.Status==GameStatus.Deleted && !service.QueryGames(GameListFilter.All,null).Any(g=>g.Id==deletedGame.Id) &&
    deletedReloaded.GetDeletedGames().Single(g=>g.Id==deletedGame.Id).Playtime==deletedGame.Playtime,
    "Fully removed game moves to separate deleted library and survives restart");

string managedSource = Folder("ManagedSource/MyGame");
string managedExe = Path.Combine(managedSource,"MyGame.exe");
string managedSave = Folder("ManagedSource/MyGame/saves");
string managedCover = Path.Combine(managedSource,"banner.png");
File.WriteAllText(managedExe,"exe"); File.WriteAllText(Path.Combine(managedSave,"slot.dat"),"save"); File.WriteAllText(managedCover,"cover");
var managed = GameStorageService.MoveToLibrary(managedSource,managedExe,managedSave,managedCover,Folder("ManagedLibrary"),CancellationToken.None);
Check(managed.Success && managed.SourceRemoved && !Directory.Exists(managedSource) && File.Exists(managed.ExecutablePath) &&
    File.Exists(Path.Combine(managed.SavePath,"slot.dat")) && File.Exists(managed.CoverPath),
    "Managed library moves complete game folder and remaps internal paths");
Check(GameStorageService.IsPlatformManaged("Steam") && !GameStorageService.IsPlatformManaged("https://example.com/game"),
    "Managed library leaves platform-controlled installations in place");

string zip = Path.Combine(root,"PackedGame.zip");
using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create))
{
    var exeEntry=archive.CreateEntry("PackedGame/PackedGame.exe"); using(var writer=new StreamWriter(exeEntry.Open())) writer.Write("exe");
    var dataEntry=archive.CreateEntry("PackedGame/data/file.dat"); using(var writer=new StreamWriter(dataEntry.Open())) writer.Write("data");
}

var extracted=ArchiveExtractionService.ExtractAndDelete(zip,Folder("ExtractedGames"),CancellationToken.None,false);
Check(extracted.Success && !extracted.SourceDeleted && File.Exists(zip) && File.Exists(Path.Combine(extracted.Destination,"PackedGame.exe")) &&
    File.Exists(Path.Combine(extracted.Destination,"data","file.dat")),
    "ZIP extraction verifies output and preserves original archive");

string unsafeZip=Path.Combine(root,"Unsafe.zip");
using(var archive=ZipFile.Open(unsafeZip,ZipArchiveMode.Create))
{ var entry=archive.CreateEntry("../outside.txt"); using var writer=new StreamWriter(entry.Open()); writer.Write("unsafe"); }
var rejectedArchive=ArchiveExtractionService.ExtractAndDelete(unsafeZip,Folder("UnsafeTarget"),CancellationToken.None,false);
Check(!rejectedArchive.Success && File.Exists(unsafeZip) && !File.Exists(Path.Combine(root,"outside.txt")),
    "Unsafe archive paths are rejected and source archive is preserved");

string detectedZip=Path.Combine(scan,"NewGame.zip"); File.Copy(unsafeZip,detectedZip);
Check(DiscoveryService.ScanFolder(scan,CancellationToken.None).Any(item=>item.IsArchive && PathSafety.Same(item.ArchivePath,detectedZip)),
    "Discovery offers supported game archives for extraction");

var setupSettings=new AppSettings();
Check(setupSettings.IsFirstRun,"IsFirstRun defaults to true for new installations");
setupSettings.IsFirstRun=false;
string setupSettingsPath=Path.Combine(root,"SetupTest","settings.json");
Directory.CreateDirectory(Path.GetDirectoryName(setupSettingsPath)!);
File.WriteAllText(setupSettingsPath,System.Text.Json.JsonSerializer.Serialize(setupSettings));
var reloaded=System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(setupSettingsPath))!;
Check(reloaded!=null && !reloaded.IsFirstRun,"IsFirstRun persists as false after first-run completion");

var addedDateGame=new Game{Id="added-date-test",Name="AddedDate Test",GameVersion="1.0",InstallSource="Local",GamePath=Folder("AddedDateGame"),Status="installed",AddedDate=new DateTime(2025,6,15,10,30,0)};
addedDateGame.ExecutablePath=Path.Combine(addedDateGame.GamePath,"test.exe");
File.WriteAllText(addedDateGame.ExecutablePath,"fixture");
service.AddGame(addedDateGame);service.SaveGames();
var addedDateLoaded=new GameService(paths);
var reloadedGame=addedDateLoaded.GetGames().Single(g=>g.Id=="added-date-test");
Check(reloadedGame.AddedDate==new DateTime(2025,6,15,10,30,0),"AddedDate persists across save/reload");
service.RemoveGame(addedDateGame);service.SaveGames();

var provenanceGame=new Game{Id="prov-test",Name="Provenance Test",GameVersion="1.0",InstallSource="Local",GamePath=Folder("ProvGame"),Status="installed"};
provenanceGame.ExecutablePath=Path.Combine(provenanceGame.GamePath,"test.exe");
File.WriteAllText(provenanceGame.ExecutablePath,"fixture");
provenanceGame.UserEditedFields.UnionWith(new[]{"Name","Notes"});
service.AddGame(provenanceGame);service.SaveGames();
var provLoaded=new GameService(paths);
var provGame=provLoaded.GetGames().Single(g=>g.Id=="prov-test");
Check(provGame.UserEditedFields.Contains("Name") && provGame.UserEditedFields.Contains("Notes") && provGame.UserEditedFields.Count==2,
    "UserEditedFields persists across save/reload");
service.RemoveGame(provenanceGame);service.SaveGames();

var f95LookupGame=new Game{Id="f95-lookup",Name="F95 Lookup",GameVersion="1.0",InstallSource="https://f95zone.to/threads/12345/",GamePath=Folder("F95LookupGame"),Status="installed",F95ThreadId="12345"};
f95LookupGame.ExecutablePath=Path.Combine(f95LookupGame.GamePath,"test.exe");
File.WriteAllText(f95LookupGame.ExecutablePath,"fixture");
service.AddGame(f95LookupGame);service.SaveGames();
Check(service.FindGameByF95ThreadId("12345")?.Id=="f95-lookup","FindGameByF95ThreadId finds correct game");
Check(service.FindGameByF95ThreadId("99999")==null,"FindGameByF95ThreadId returns null for unknown thread ID");
service.RemoveGame(f95LookupGame);service.SaveGames();

var portProbe=new TcpListener(IPAddress.Loopback,0);portProbe.Start();int remotePort=((IPEndPoint)portProbe.LocalEndpoint).Port;portProbe.Stop();
using(var remoteHost=new RemoteHostService())
{
    remoteHost.Start(remotePort,8,45,640);
    using var remoteClient=new HttpClient();
    string remotePage=await remoteClient.GetStringAsync($"http://127.0.0.1:{remotePort}/");
    Check(remotePage.Contains("Game Remote")&&remotePage.Contains("Zum Home-Bildschirm")&&remotePage.Contains("frameAck")&&remotePage.Contains("remoteCompact")&&remoteHost.Pin.Length==6,
        "Serve the local iPhone/iPad web app with a rotating session PIN");
    bool wrongPinRejected=false;using(var wrongSocket=new ClientWebSocket())try{await wrongSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{remotePort}/control?pin=000000"),CancellationToken.None);}catch(WebSocketException){wrongPinRejected=true;}
    Check(wrongPinRejected,"Reject an incorrect remote-control PIN");
    using var socket=new ClientWebSocket();
    await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{remotePort}/control?pin={remoteHost.Pin}"),CancellationToken.None);
    await Task.Delay(100);
    Check(socket.State==WebSocketState.Open&&remoteHost.ClientCount==1,"Require the correct PIN before opening the protected WebSocket");
    socket.Abort();remoteHost.Stop();
}
HardeningChecks.Run(root, Check);
await DownloadChecks.Run(root, Check);
Console.WriteLine($"All {passed} checks passed. Fixtures: {root}");

if (args.Length == 2 && args[0] == "--process-test")
{
    using var launcher = new GameLaunchService();
    var ended = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
    int eventCount = 0;
    launcher.SessionEnded += (_, duration) => { Interlocked.Increment(ref eventCount); ended.TrySetResult(duration); };
    string exe = Path.GetFullPath(args[1]);
    var processGame = new Game { Id = "process-test", Name = "Process test", GamePath = Path.GetDirectoryName(exe)!, ExecutablePath = exe, IsInstalled = true };
    Check(launcher.TryLaunch(processGame), "Start real fixture process");
    Check(!launcher.TryLaunch(processGame), "Reject duplicate launch");
    await Task.Delay(3500);
    Check(launcher.IsRunning(processGame.Id), "Keep tracking child after parent exits");
    Check(launcher.MinimizeGames() > 0, "Minimize tracked child window");
    await Task.Delay(400);
    Check(launcher.RestoreGames() > 0, "Restore tracked child window");
    var checkpoint = launcher.Checkpoint().Single();
    var remainder = await ended.Task.WaitAsync(TimeSpan.FromSeconds(25));
    Check(eventCount == 1 && !launcher.HasRunningGames, "One session-ended event and clean state");
    Check(checkpoint.Duration + remainder > TimeSpan.FromSeconds(12) && checkpoint.Duration + remainder < TimeSpan.FromSeconds(20),
        "Playtime covers child session without double counting checkpoint");
    Console.WriteLine("Process integration checks passed.");
}

sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}
