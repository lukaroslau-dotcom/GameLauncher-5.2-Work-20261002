using GameLauncher.Constants;
using GameLauncher.models;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace GameLauncher.Service
{
    public class ScannedExecutable
    {
        public string Name { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public string GamePath { get; set; } = "";
        public string Version { get; set; } = "Unknown";
        public string Source { get; set; } = "Lokaler Ordner";
        public string InstallSource { get; set; } = "";
        public string SavePath { get; set; } = "";
        public string CoverPath { get; set; } = "";
        public string ArchivePath { get; set; } = "";
        public int Confidence { get; set; } = 30;
        public bool IsArchive => ArchivePath.Length > 0;
        public string DisplayPath => IsArchive ? ArchivePath : ExecutablePath;
        public string ActionLabel => IsArchive ? "Entpacken" : "Hinzufügen";
        public string DetectionLabel => IsArchive ? Source + " · wird in den Spieleordner entpackt" :
            Confidence >= 90 ? Source + " · Spiel erkannt" : Source + " · Bitte prüfen";
    }

    public class ImportService
    {
        public sealed record ImportPreview(string Sha256, int Games, int ExistingIds, int MissingInstallations);
        private static byte[] ReadImport(string path)
        {
            PathSafety.RejectLinks(path);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > 32L * 1024 * 1024) throw new IOException("Import ist größer als 32 MB.");
            using var buffer = new MemoryStream(); input.CopyTo(buffer); return buffer.ToArray();
        }
        public ImportPreview Preview(string path, GameService service)
        {
            var bytes = ReadImport(path);
            var entries = JsonSerializer.Deserialize<List<Game>>(bytes) ?? throw new JsonException("Ungültige Spieleliste.");
            if (entries.Count > 100000 || entries.Any(g => g == null)) throw new JsonException("Ungültige Spieleliste.");
            return new(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), entries.Count,
                entries.Count(g => service.ShowGame(g.Id) != null), entries.Count(g => !string.IsNullOrWhiteSpace(g.GamePath) && !Directory.Exists(g.GamePath)));
        }
        public (int added, int skipped, string? error) ImportGamesJson(string filePath, GameService gameService, string? expectedSha256 = null)
        {
            var additions = new List<Game>();
            int skipped = 0;
            try
            {
                byte[] source = ReadImport(filePath);
                string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source));
                if (expectedSha256 != null && hash != expectedSha256) throw new IOException("Importdatei wurde nach der Vorschau verändert.");
                var imported = JsonSerializer.Deserialize<List<Game>>(source);
                if (imported == null || imported.Any(g => g == null)) throw new JsonException("Invalid game list.");
                if (imported.Count > 100000) throw new JsonException("Zu viele Spiele im Import.");
                var knownIds = gameService.GetGames().Select(g => g.Id).ToHashSet();
                var knownPaths = gameService.GetGames().Where(g => g.ExecutablePath.Length > 0)
                    .Select(g => PathSafety.Full(g.ExecutablePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                int index = 0;
                foreach (var game in imported)
                {
                    if (string.IsNullOrWhiteSpace(game.Id)) game.Id = "import-" + hash[..24] + "-" + index;
                    index++;
                    GameService.NormalizeGame(game);
                    if (!knownIds.Add(game.Id) ||
                        (game.ExecutablePath.Length > 0 && !knownPaths.Add(PathSafety.Full(game.ExecutablePath))))
                    { skipped++; continue; }
                    additions.Add(game);
                }
                if (additions.Count > 0)
                {
                    string originals = Path.Combine(gameService.Paths.ArchivePath, "ImportOriginals");
                    PathSafety.RejectLinks(originals); Directory.CreateDirectory(originals);
                    string original = Path.Combine(originals, hash + ".json"); PathSafety.RejectLinks(original);
                    if (!File.Exists(original)) { using var output = new FileStream(original, FileMode.CreateNew); output.Write(source); output.Flush(true); }
                }
                foreach (var game in additions) gameService.AddGame(game);
                if (additions.Count > 0) gameService.SaveGames();
                return (additions.Count, skipped, null);
            }
            catch (Exception ex)
            {
                foreach (var game in additions) gameService.RemoveGame(game);
                Debug.WriteLine(ex);
                return (0, skipped, "Import fehlgeschlagen: " + ex.Message);
            }
        }

        public List<ScannedExecutable> ScanFolder(string folder) => DiscoveryService.ScanFolder(folder, CancellationToken.None);
    }
}
