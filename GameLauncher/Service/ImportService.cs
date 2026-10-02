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
        public (int added, int skipped, string? error) ImportGamesJson(string filePath, GameService gameService)
        {
            var additions = new List<Game>();
            int skipped = 0;
            try
            {
                var imported = JsonSerializer.Deserialize<List<Game>>(File.ReadAllText(filePath));
                if (imported == null || imported.Any(g => g == null)) throw new JsonException("Invalid game list.");
                var knownIds = gameService.GetGames().Select(g => g.Id).ToHashSet();
                var knownPaths = gameService.GetGames().Where(g => g.ExecutablePath.Length > 0)
                    .Select(g => PathSafety.Full(g.ExecutablePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var game in imported)
                {
                    GameService.NormalizeGame(game);
                    if (!knownIds.Add(game.Id) ||
                        (game.ExecutablePath.Length > 0 && !knownPaths.Add(PathSafety.Full(game.ExecutablePath))))
                    { skipped++; continue; }
                    additions.Add(game);
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
