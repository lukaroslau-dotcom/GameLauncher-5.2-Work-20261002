using GameLauncher.Constants;
using GameLauncher.models;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace GameLauncher.Service
{
    public class GameService
    {
        private List<Game> games = new List<Game>();
        private AppPaths paths;
        private bool writesBlocked;
        private string? lastSavedJson;
        private long lastSavedFileLength = -1;
        private DateTime lastSavedWriteUtc;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public string? LoadError { get; private set; }

        public AppPaths Paths => paths;

        public GameService(AppPaths? appPaths = null)
        {
            paths = appPaths ?? AppPaths.Default();
            paths.EnsureCreated();
            LoadGames();
        }

        public void UsePaths(AppPaths newPaths)
        {
            paths = newPaths;
            paths.EnsureCreated();
            LoadGames();
        }

        public bool TryCopyDataTo(AppPaths destination, out string error)
        {
            error = "";
            if (PathSafety.Same(paths.ArchivePath, destination.ArchivePath)) return true;
            try
            {
                if (PathSafety.Overlaps(paths.ArchivePath, destination.ArchivePath))
                    throw new IOException("Die Archivordner duerfen sich nicht ueberschneiden.");
                if (Directory.Exists(destination.ArchivePath) && Directory.EnumerateFileSystemEntries(destination.ArchivePath).Any())
                    throw new IOException("Bitte einen leeren Zielordner waehlen.");
                SaveGames();
                var originalFiles = VerifiedSaveBackup.Snapshot(paths.ArchivePath);
                PathSafety.CopyTree(paths.ArchivePath, destination.ArchivePath);
                VerifiedSaveBackup.AssertEqual(originalFiles, VerifiedSaveBackup.Snapshot(destination.ArchivePath));
                VerifiedSaveBackup.AssertEqual(originalFiles, VerifiedSaveBackup.Snapshot(paths.ArchivePath));
                var copied = JsonSerializer.Deserialize<List<Game>>(File.ReadAllText(destination.GamesFilePath))!;
                foreach (var game in copied)
                {
                    if (game.CoverPath.Length > 0 && PathSafety.Within(game.CoverPath, paths.ArchivePath))
                        game.CoverPath = Path.Combine(destination.ArchivePath, Path.GetRelativePath(paths.ArchivePath, game.CoverPath));
                    game.GalleryPaths=(game.GalleryPaths??new()).Where(path=>!string.IsNullOrWhiteSpace(path)).Select(path=>PathSafety.Within(path,paths.ArchivePath)
                        ?Path.Combine(destination.ArchivePath,Path.GetRelativePath(paths.ArchivePath,path)):path).ToList();
                }
                File.WriteAllText(destination.GamesFilePath, JsonSerializer.Serialize(copied, JsonOptions));
                File.Copy(destination.GamesFilePath, destination.GamesFilePath + ".bak", true);
                return true;
            }
            catch (Exception ex) { Debug.WriteLine(ex); error = ex.Message; return false; }
        }

        public void AddGame(Game game)
        {
            if (game == null)
                return;

            NormalizeGame(game);
            if (ShowGame(game.Id) != null) throw new InvalidOperationException("Duplicate game ID.");
            games.Add(game);
        }

        public void RemoveGame(Game game)
        {
            if (game == null)
                return;

            games.Remove(game);
        }

        public List<Game> GetGames()
        {
            return games.ToList();
        }

        public List<Game> GetInstalledGames()
        {
            return QueryGames(GameListFilter.Installed, null);
        }

        public List<Game> GetWaitingForUpdatesGames()
        {
            return QueryGames(GameListFilter.WaitingForUpdates, null);
        }

        public List<Game> GetFinishedGames()
        {
            return QueryGames(GameListFilter.Finished, null);
        }

        public List<Game> GetDeletedGames()
        {
            return QueryGames(GameListFilter.Deleted, null);
        }

        public List<Game> QueryGames(GameListFilter filter, string? search)
        {
            IEnumerable<Game> query = games;

            query = filter switch
            {
                GameListFilter.Wishlist => games.Where(g => g.Status == GameStatus.Wishlist),
                GameListFilter.Installed => games.Where(g =>
                    g.IsInstalled && g.Status == GameStatus.Installed),
                GameListFilter.WaitingForUpdates => games.Where(g =>
                    g.Status == GameStatus.WaitingForUpdates),
                GameListFilter.Finished => games.Where(g =>
                    g.Status == GameStatus.Finished),
                GameListFilter.Deleted => games.Where(g =>
                    g.Status == GameStatus.Deleted),
                GameListFilter.Favorites => games.Where(g =>
                    g.IsFavorite && g.Status != GameStatus.Deleted),
                GameListFilter.NeedsAttention => games.Where(g =>
                    g.Status != GameStatus.Deleted && g.PathWarning.Length > 0),
                GameListFilter.UpdatesAvailable => games.Where(g =>
                    g.Status != GameStatus.Deleted && g.UpdateAvailable),
                GameListFilter.NeverPlayed => games.Where(g =>
                    g.Status != GameStatus.Deleted && g.IsInstalled && g.LastPlayed == null),
                _ => games.Where(g => g.Status != GameStatus.Deleted)
            };

            if (!string.IsNullOrWhiteSpace(search))
            {
                string searchTerm = search.Trim();
                var exact = query.Where(g => MatchesSearch(g, searchTerm, false)).ToList();
                query = exact.Count > 0 ? exact : query.Where(g => MatchesSearch(g, searchTerm));
            }

            return query
                .OrderByDescending(g => g.IsFavorite)
                .ThenByDescending(g => g.LastPlayed ?? DateTime.MinValue)
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public Game? ShowGame(string gameId)
        {
            return games.FirstOrDefault(g => g.Id == gameId);
        }

        public void SaveGames()
        {
            if (writesBlocked) throw new IOException("Die beschaedigte Bibliothek ist vor dem Ueberschreiben geschuetzt.");
            paths.EnsureCreated();

            string json = JsonSerializer.Serialize(games, JsonOptions);
            if (json == lastSavedJson && File.Exists(paths.GamesFilePath))
            {
                var current = new FileInfo(paths.GamesFilePath);
                if (current.Length == lastSavedFileLength && current.LastWriteTimeUtc == lastSavedWriteUtc) return;
            }
            string tempFilePath = paths.GamesFilePath + ".tmp";

            File.WriteAllText(tempFilePath, json);
            if (File.Exists(paths.GamesFilePath))
                File.Replace(tempFilePath, paths.GamesFilePath, paths.GamesFilePath + ".bak");
            else File.Move(tempFilePath, paths.GamesFilePath);
            lastSavedJson = json;
            RememberSavedFile();
        }

        public void LoadGames()
        {
            LoadError = null;
            writesBlocked = false;
            lastSavedJson = null;
            lastSavedFileLength = -1;
            lastSavedWriteUtc = default;
            paths.EnsureCreated();

            if (!File.Exists(paths.GamesFilePath))
            {
                games = new List<Game>();
                return;
            }

            try
            {
                string json = File.ReadAllText(paths.GamesFilePath);
                games = JsonSerializer.Deserialize<List<Game>>(json) ?? new List<Game>();
                NormalizeStatuses();
                lastSavedJson = json;
                RememberSavedFile();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Fehler beim Laden der Spiele: {ex.Message}");
                try
                {
                    string backupPath = paths.GamesFilePath + ".corrupt-" + Guid.NewGuid().ToString("N");
                    File.Copy(paths.GamesFilePath, backupPath, true);
                }
                catch (Exception backupEx)
                {
                    Debug.WriteLine($"Backup fehlgeschlagen: {backupEx.Message}");
                }
                try
                {
                    string safeCopy = paths.GamesFilePath + ".bak";
                    if (!File.Exists(safeCopy)) throw new FileNotFoundException("Keine Sicherung vorhanden.", safeCopy);
                    string json = File.ReadAllText(safeCopy);
                    games = JsonSerializer.Deserialize<List<Game>>(json) ?? new List<Game>();
                    NormalizeStatuses();
                    File.Copy(safeCopy, paths.GamesFilePath, true);
                    lastSavedJson = json;
                    RememberSavedFile();
                    LoadError = "Die Bibliotheksdatei war beschädigt und wurde automatisch aus der letzten sicheren Kopie wiederhergestellt.";
                    return;
                }
                catch (Exception recoveryEx)
                {
                    Debug.WriteLine($"Wiederherstellung fehlgeschlagen: {recoveryEx.Message}");
                    writesBlocked = true;
                    LoadError = "Die Bibliothek konnte nicht gelesen oder wiederhergestellt werden. Die Originaldatei ist vor dem Überschreiben geschützt.";
                    games = new List<Game>();
                }
            }
        }

        public void UpdateStatus(string selectedGameID, string newStatus)
        {
            Game? existing = games.FirstOrDefault(g => g.Id == selectedGameID);

            if (existing == null)
                return;

            existing.Status = GameStatus.Normalize(newStatus);
            // Status is independent of installed files; no path or installation mutation.
        }

        public void EditGame(string selectedGameID, Game updatedGame)
        {
            Game? existing = games.FirstOrDefault(g => g.Id == selectedGameID);

            if (existing == null || updatedGame == null)
            {
                Debug.WriteLine("Fehler: Das Spiel konnte nicht bearbeitet werden.");
                return;
            }

            existing.CoverFocusX=updatedGame.CoverFocusX;
            existing.CoverFocusY=updatedGame.CoverFocusY;
            existing.Notes = updatedGame.Notes;
            existing.VersionInfo = updatedGame.VersionInfo;
            existing.Name = updatedGame.Name;
            existing.CoverPath = updatedGame.CoverPath;
            existing.GamePath = updatedGame.GamePath;
            existing.ExecutablePath = updatedGame.ExecutablePath;
            existing.SavePath = updatedGame.SavePath;
            existing.InstallSource = updatedGame.InstallSource;
            existing.ModUrl = updatedGame.ModUrl;
            existing.GalleryPaths = updatedGame.GalleryPaths;
            existing.GameVersion = updatedGame.GameVersion;
            existing.IsInstalled = updatedGame.IsInstalled;
            existing.Status = updatedGame.Status;
            existing.IsFavorite = updatedGame.IsFavorite;
            existing.LastPlayed = updatedGame.LastPlayed;
            existing.Playtime = updatedGame.Playtime;
            // Update metadata belongs to the stable library entry and must survive edits.
        }

        public void ApplyReinstall(Game game, string gamePath, string executablePath, string savePath)
        {
            if (!string.IsNullOrWhiteSpace(game.GamePath) && !PathSafety.Same(game.GamePath, gamePath))
                game.InstallationHistory.Add(new(game.GamePath, game.ExecutablePath, game.SavePath, game.GameVersion, DateTime.UtcNow));
            game.GamePath = gamePath;
            game.ExecutablePath = executablePath;
            game.SavePath = savePath;
            game.IsInstalled = true;
            game.Status = GameStatus.Installed;
        }

        public void ClearLocalPathsKeepHistory(Game game)
        {
            game.GamePath = "";
            game.ExecutablePath = "";
            game.SavePath = "";
            game.IsInstalled = false;
            game.Status = GameStatus.Deleted;
        }

        public bool ArchiveSave(Game game)
        {
            try
            {
                if (!Directory.Exists(game.SavePath)) return false;
                if (!string.IsNullOrWhiteSpace(game.GamePath) && PathSafety.Within(game.GamePath, game.SavePath)) return false;
                if (PathSafety.Overlaps(game.SavePath, paths.ArchivePath)) return false;
                string archive = paths.GetSaveArchiveForGame(game.Id);
                string stage = archive + ".pending-" + Guid.NewGuid().ToString("N");
                VerifiedSaveBackup.Create(game.SavePath, stage);
                string history = Path.Combine(paths.ArchivePath, "SaveHistory", game.Id);
                CommitDirectory(stage, archive, history);
                try { VerifiedSaveBackup.RetireOlder(history, Path.Combine(paths.ArchivePath, "RetiredSaves", game.Id)); }
                catch (Exception retentionError) { Debug.WriteLine(retentionError); } // A verified backup is never discarded on retention failure.
                return true;
            }
            catch (Exception ex) { Debug.WriteLine(ex); return false; }
        }

        private static void CommitDirectory(string stage, string destination, string? history = null)
        {
            if (history != null) Directory.CreateDirectory(history);
            string backup = history != null ? Path.Combine(history, Guid.NewGuid().ToString("N"))
                : destination + ".previous-" + Guid.NewGuid().ToString("N");
            bool hadDestination = Directory.Exists(destination);
            if (hadDestination) { PathSafety.RejectTreeLinks(destination); Directory.Move(destination, backup); }
            try { Directory.Move(stage, destination); }
            catch
            {
                if (hadDestination && !Directory.Exists(destination)) Directory.Move(backup, destination);
                throw;
            }
        }

        public bool HasArchivedSave(string gameId)
        {
            string archive = paths.GetSaveArchiveForGame(gameId);
            if (!Directory.Exists(archive))
                return false;

            return Directory.EnumerateFileSystemEntries(archive).Any();
        }

        public bool DestinationHasFiles(string destination)
        {
            if (string.IsNullOrWhiteSpace(destination) || !Directory.Exists(destination))
                return false;

            return Directory.EnumerateFileSystemEntries(destination).Any();
        }

        public bool RestoreSave(string gameId, string destination, SaveRestoreMode mode)
        {
            try
            {
                if (mode == SaveRestoreMode.Cancel || !HasArchivedSave(gameId) || string.IsNullOrWhiteSpace(destination)) return false;
                string target = PathSafety.Full(destination);
                string source = paths.GetSaveArchiveForGame(gameId);
                if (PathSafety.Overlaps(target, paths.ArchivePath)) return false;
                string[] protectedFolders = { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop), AppContext.BaseDirectory };
                if (PathSafety.Same(target, Path.GetPathRoot(target)!) ||
                    protectedFolders.Where(p => p.Length > 0).Any(p => PathSafety.Within(p, target)) ||
                    PathSafety.Within(target, Environment.GetFolderPath(Environment.SpecialFolder.Windows))) return false;
                if (games.Any(g => g.GamePath.Length > 0 && PathSafety.Within(g.GamePath, target))) return false;
                PathSafety.RejectLinks(target);
                string stage = target + ".pending-" + Guid.NewGuid().ToString("N");
                if (mode == SaveRestoreMode.Merge && Directory.Exists(target)) PathSafety.CopyTree(target, stage);
                VerifiedSaveBackup.CopyPayload(source, stage);
                CommitDirectory(stage, target);
                return true;
            }
            catch (Exception ex) { Debug.WriteLine(ex); return false; }
        }

        public bool TryDeleteInstallation(Game game, out string error)
        {
            error = "";

            if (game == null)
            {
                error = "Kein Spiel ausgewählt.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(game.GamePath) || !Directory.Exists(game.GamePath))
                return true;

            if (!paths.CanSafelyDeleteGamePath(game.GamePath) ||
                string.IsNullOrWhiteSpace(game.ExecutablePath) ||
                !PathSafety.Within(game.ExecutablePath, game.GamePath) ||
                games.Any(other => other.Id != game.Id && !string.IsNullOrWhiteSpace(other.GamePath) &&
                    PathSafety.Overlaps(other.GamePath, game.GamePath)))
            {
                error = "Der Game-Ordner kann nicht sicher gelöscht werden.";
                Debug.WriteLine($"Unsicherer Delete-Pfad: {game.GamePath}");
                return false;
            }

            try
            {
                InstallationRecovery.Remove(paths, game);
                game.IsInstalled = false;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Fehler beim Löschen der Installation: {ex.Message}");
                error = "Das Spiel konnte nicht gelöscht werden.";
                return false;
            }
        }

        public bool TryDeleteArchivedSave(string gameId, out string error)
        {
            error = "";
            string archive = paths.GetSaveArchiveForGame(gameId);

            try
            {
                if (Directory.Exists(archive))
                {
                    PathSafety.RejectTreeLinks(archive);
                    Directory.Delete(archive, true);
                }
                string history = Path.Combine(paths.ArchivePath, "SaveHistory", gameId);
                if (Directory.Exists(history)) { PathSafety.RejectTreeLinks(history); Directory.Delete(history, true); }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Fehler beim Löschen des Save-Archivs: {ex.Message}");
                error = "Das Save-Archiv konnte nicht gelöscht werden.";
                return false;
            }
        }

        private void NormalizeStatuses()
        {
            if (games.Any(g => g == null)) throw new JsonException("Null game.");
            foreach (var game in games) NormalizeGame(game);
            if (games.Select(g => g.Id).Distinct().Count() != games.Count) throw new JsonException("Duplicate game IDs.");
        }

        public static void NormalizeGame(Game game)
        {
            if (string.IsNullOrWhiteSpace(game.Id)) game.Id = Guid.NewGuid().ToString();
            if (game.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || game.Id is "." or ".." ||
                game.Id.EndsWith('.') || game.Id.EndsWith(' ')) throw new JsonException("Invalid game ID.");
            game.Notes ??= "";
            game.VersionInfo ??= "";
            game.Name ??= "";
            game.CoverPath ??= "";
            game.GamePath ??= "";
            game.ExecutablePath ??= "";
            game.SavePath ??= "";
            game.GameVersion ??= "";
            game.InstallSource ??= "";
            game.F95ThreadId ??= "";
            game.LatestKnownVersion ??= "";
            game.IgnoredUpdateVersion ??= "";
            game.F95LastChanged ??= "";
            game.UpdateCheckError ??= "";
            game.F95Developer ??= "";
            game.F95Status ??= "";
            game.ModUrl ??= "";
            game.GalleryPaths ??= new();
            game.InstallationHistory ??= new();
            game.Aliases ??= new();
            game.Tags ??= new();
            game.LaunchArguments ??= "";
            game.GalleryPaths=game.GalleryPaths.Where(path=>!string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!F95UpdateService.TryExtractThreadId(game.InstallSource, out _))
            {
                game.F95ThreadId = "";
                game.LatestKnownVersion = "";
                game.IgnoredUpdateVersion = "";
                game.F95LastChanged = "";
                game.F95Developer = "";
                game.F95Status = "";
                game.UpdateCheckError = "";
            }
            game.UpdateAvailable = F95UpdateService.TryExtractThreadId(game.InstallSource, out _) &&
                F95UpdateService.IsUpdateAvailable(game.GameVersion, game.LatestKnownVersion, game.IgnoredUpdateVersion);
            game.Status = GameStatus.Normalize(game.Status);
            game.IsInstalled = game.IsInstalled || (game.Status == GameStatus.Installed && game.ExecutablePath.Length > 0);
            if (game.Playtime < TimeSpan.Zero) game.Playtime = TimeSpan.Zero;
            if (game.AddedDate == default || game.AddedDate == DateTime.MinValue)
                game.AddedDate = game.LastPlayed ?? DateTime.MinValue;
        }

        public Game? FindExecutable(string exe) => games.FirstOrDefault(g =>
            !string.IsNullOrWhiteSpace(g.ExecutablePath) && PathSafety.Same(g.ExecutablePath, exe));
        public Game? FindGameByF95ThreadId(string threadId) => games.FirstOrDefault(g =>
            !string.IsNullOrWhiteSpace(g.F95ThreadId) && g.F95ThreadId == threadId);
        public static string GetPathWarning(Game game)
        {
            if (!game.IsInstalled || game.Status != GameStatus.Installed) return "";
            try
            {
                if (string.IsNullOrWhiteSpace(game.GamePath) || !Directory.Exists(game.GamePath)) return "Spielordner fehlt";
                if (string.IsNullOrWhiteSpace(game.ExecutablePath) || !File.Exists(game.ExecutablePath)) return "Startdatei fehlt";
                if (!PathSafety.Within(game.ExecutablePath, game.GamePath)) return "Startdatei liegt außerhalb des Spielordners";
                return "";
            }
            catch { return "Spielpfad ist nicht erreichbar"; }
        }
        private void RememberSavedFile()
        {
            var file = new FileInfo(paths.GamesFilePath);
            lastSavedFileLength = file.Exists ? file.Length : -1;
            lastSavedWriteUtc = file.Exists ? file.LastWriteTimeUtc : default;
        }
        public void ExportGames(string destination) => File.WriteAllText(destination, JsonSerializer.Serialize(games, JsonOptions));
        public string CacheCover(string source, string id)
        {
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return source;
            _ = paths.GetSaveArchiveForGame(id);
            string folder = Path.Combine(paths.GamesDataPath, "Covers");
            Directory.CreateDirectory(folder);
            string destination = Path.Combine(folder, id + Path.GetExtension(source));
            if (!PathSafety.Same(source, destination)) File.Copy(source, destination, true);
            foreach (string oldCover in Directory.EnumerateFiles(folder, id + ".*"))
                if (!PathSafety.Same(oldCover, destination))
                {
                    try { ImageCache.Invalidate(oldCover); File.Delete(oldCover); }
                    catch (Exception ex) { Debug.WriteLine($"Altes Cover konnte nicht entfernt werden: {ex.Message}"); }
                }
            ImageCache.Invalidate(destination);
            DeleteTemporaryCover(source, destination);
            return destination;
        }

        public void DeleteTemporaryCover(string? source, string? retainedPath = null)
        {
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source) ||
                (!string.IsNullOrWhiteSpace(retainedPath) && PathSafety.Same(source, retainedPath))) return;
            string previewFolder = Path.Combine(paths.GamesDataPath, "F95Preview");
            try
            {
                if (PathSafety.Within(source, previewFolder))
                {
                    ImageCache.Invalidate(source);
                    File.Delete(source);
                }
            }
            catch (Exception ex) { Debug.WriteLine($"Temporäres Cover konnte nicht entfernt werden: {ex.Message}"); }
        }

        private static bool MatchesSearch(Game game, string searchTerm, bool allowFuzzy = true)
        {
            return LocalGameSearch.Matches(game, searchTerm, allowFuzzy) ||
                   game.GameVersion.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                   game.LatestKnownVersion.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                   game.F95Developer.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                   game.Notes.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                   game.VersionInfo.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                   game.InstallSource.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                   game.Status.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);
        }

    }
}
