using System.Diagnostics;
using System.IO;

namespace GameLauncher.Service
{
    public class AppPaths
    {
        public string ArchivePath { get; }
        public string GamesDataPath { get; }
        public string GamesFilePath { get; }
        public string SaveArchivePath { get; }
        public string SettingsFilePath { get; }

        public AppPaths(string archivePath)
        {
            ArchivePath = Path.GetFullPath(archivePath);
            GamesDataPath = Path.Combine(ArchivePath, "GamesData");
            GamesFilePath = Path.Combine(GamesDataPath, "games.json");
            SaveArchivePath = Path.Combine(ArchivePath, "SaveArchive");
            SettingsFilePath = Path.Combine(GamesDataPath, "settings.json");
        }

        public static string LegacyArchivePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Visual Studio 18",
            "GameLauncher",
            "Archive"
        );

        // The development edition never opens the original 5.2 archive implicitly.
        public static string DefaultArchivePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameLauncher.5.2", "Archive");

        public static AppPaths Default()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--data-root");
            if (index >= 0 && index + 1 < args.Length) return new AppPaths(args[index + 1]);
            return new AppPaths(DefaultArchivePath);
        }

        public void EnsureCreated()
        {
            Directory.CreateDirectory(GamesDataPath);
            Directory.CreateDirectory(SaveArchivePath);
        }

        public string GetSaveArchiveForGame(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId) || gameId is "." or ".." ||
                gameId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                gameId.EndsWith('.') || gameId.EndsWith(' '))
                throw new ArgumentException("Invalid game ID.", nameof(gameId));
            return Path.Combine(SaveArchivePath, gameId);
        }

        public bool CanSafelyDeleteGamePath(string? gamePath)
        {
            if (string.IsNullOrWhiteSpace(gamePath))
                return false;

            string fullGamePath;
            try
            {
                fullGamePath = Path.GetFullPath(gamePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ungültiger GamePath: {ex.Message}");
                return false;
            }

            if (!Directory.Exists(fullGamePath))
                return false;

            try { PathSafety.RejectTreeLinks(fullGamePath); }
            catch (IOException) { return false; }

            if (PathSafety.Within(fullGamePath, ArchivePath) ||
                PathSafety.Overlaps(fullGamePath, AppContext.BaseDirectory) ||
                PathSafety.Within(fullGamePath, Environment.GetFolderPath(Environment.SpecialFolder.Windows)))
                return false;

            if (fullGamePath.Length < 8)
                return false;

            string? root = Path.GetPathRoot(fullGamePath);
            if (!string.IsNullOrEmpty(root) &&
                string.Equals(
                    fullGamePath,
                    root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (string protectedPath in GetProtectedDirectories())
            {
                if (string.IsNullOrWhiteSpace(protectedPath))
                    continue;

                string fullProtected;
                try
                {
                    fullProtected = Path.GetFullPath(protectedPath)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch
                {
                    continue;
                }

                if (string.Equals(fullGamePath, fullProtected, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (IsAncestorOf(fullGamePath, fullProtected))
                    return false;
            }

            return true;
        }

        private IEnumerable<string> GetProtectedDirectories()
        {
            yield return ArchivePath;
            yield return GamesDataPath;
            yield return SaveArchivePath;
            yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.System);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        private static bool IsAncestorOf(string ancestor, string descendant)
        {
            string prefix = ancestor + Path.DirectorySeparatorChar;
            return descendant.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
