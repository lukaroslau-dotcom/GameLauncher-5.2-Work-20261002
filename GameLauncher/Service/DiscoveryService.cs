using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameLauncher.Service;

public sealed class DiscoveryService : IDisposable
{
    private readonly List<FileSystemWatcher> watchers = new();
    private readonly System.Threading.Timer debounce;
    private bool paused;
    public event Action? Changed;
    public DiscoveryService() => debounce = new(_ => { if (!paused) Changed?.Invoke(); }, null, Timeout.Infinite, Timeout.Infinite);
    public void Pause(bool value) { paused = value; if (value) debounce.Change(Timeout.Infinite, Timeout.Infinite); }

    public void Watch(IEnumerable<string> folders)
    {
        foreach (var watcher in watchers) watcher.Dispose();
        watchers.Clear();
        foreach (string folder in folders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                watcher.Created += OnChanged;
                watcher.Renamed += OnChanged;
                watcher.Changed += OnChanged;
                watcher.Error += (_, _) => Schedule();
                watchers.Add(watcher);
            }
            catch (Exception ex) { Debug.WriteLine(ex.Message); }
        }
    }
    private void OnChanged(object sender, FileSystemEventArgs args)
    {
        string ext = Path.GetExtension(args.FullPath);
        if (ext is ".exe" or ".acf" or ".item" or ".zip" or ".rar" or ".7z" or "" || ext == ".vdf") Schedule();
    }
    private void Schedule() { if (!paused) debounce.Change(8000, Timeout.Infinite); }

    public static List<string> DefaultRoots(string gameFolder)
    {
        var folders = new List<string>();
        if (!string.IsNullOrWhiteSpace(gameFolder)) folders.Add(gameFolder);
        folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        folders.Add(Path.Combine(AppPaths.DefaultArchivePath, "..", "GameDownloads"));
        return folders.Where(Directory.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<string> WatchRoots(string gameFolder)
    {
        var roots = DefaultRoots(gameFolder);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            string steam = key?.GetValue("SteamPath") as string ?? "";
            if (steam.Length > 0) roots.Add(Path.Combine(steam, "steamapps"));
        }
        catch (Exception ex) { Debug.WriteLine(ex.Message); }
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests"));
        return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<ScannedExecutable> Scan(IEnumerable<string> roots, CancellationToken cancellation)
    {
        var result = new List<ScannedExecutable>();
        foreach (string root in roots)
            result.AddRange(ScanFolder(root, cancellation));
        ReadSteam(result, cancellation);
        ReadEpic(result, cancellation);
        return result.GroupBy(g => PathSafety.Full(g.IsArchive ? g.ArchivePath : g.ExecutablePath), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Confidence).First())
            .OrderByDescending(g => g.Confidence).ThenBy(g => g.Name).ToList();
    }

    public static List<ScannedExecutable> ScanFolder(string folder, CancellationToken cancellation)
    {
        var result = new List<ScannedExecutable>();
        var files = EnumerateCandidates(folder, cancellation).ToList();
        foreach (string exe in files.Where(path => Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)))
        {
            var candidate = Describe(exe);
            if (candidate != null) result.Add(candidate);
        }
        // Engines often ship helper binaries; the outer game executable is the usable entry point.
        var executables = result.Where(candidate => !result.Any(other =>
                other != candidate && other.Confidence >= candidate.Confidence &&
                !PathSafety.Same(other.GamePath, candidate.GamePath) &&
                PathSafety.Within(candidate.GamePath, other.GamePath)))
            .GroupBy(g => g.GamePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(g => g.Confidence).First()).ToList();
        executables.AddRange(files.Where(ArchiveExtractionService.IsSupported).Select(archive => new ScannedExecutable
        {
            Name = Path.GetFileNameWithoutExtension(archive), ArchivePath = archive,
            Source = Path.GetExtension(archive).TrimStart('.').ToUpperInvariant() + "-Archiv", Confidence = 80
        }));
        return executables;
    }

    private static IEnumerable<string> EnumerateCandidates(string root, CancellationToken cancellation)
    {
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        int visited = 0;
        while (pending.TryPop(out var next) && visited++ < 15000)
        {
            cancellation.ThrowIfCancellationRequested();
            FileSystemInfo[] entries;
            try { entries = new DirectoryInfo(next.Path).GetFileSystemInfos(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var entry in entries)
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (entry is DirectoryInfo)
                {
                    if (next.Depth < 6 && !IgnoredDirectory(entry.Name)) pending.Push((entry.FullName, next.Depth + 1));
                }
                else if (entry.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ArchiveExtractionService.IsSupported(entry.FullName)) yield return entry.FullName;
            }
        }
    }
    private static bool IgnoredDirectory(string name) =>
        new[] { "node_modules", ".git", "Windows", "redist", "_CommonRedist", "Engine", "lib", "renpy", "cache", "saves" }
            .Contains(name, StringComparer.OrdinalIgnoreCase);

    public static ScannedExecutable? Describe(string exe)
    {
        string name = Path.GetFileNameWithoutExtension(exe);
        if (new[] { "unins", "setup", "install", "crash", "helper", "updater", "unitycrash", "notification", "cef", "python" }
            .Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase))) return null;
        string folder = Path.GetDirectoryName(exe)!;
        var candidate = new ScannedExecutable
        {
            Name = name.Replace('_', ' '), ExecutablePath = exe, GamePath = folder, Source = "Lokaler Ordner"
        };
        try
        {
            var version = FileVersionInfo.GetVersionInfo(exe);
            candidate.Version = string.IsNullOrWhiteSpace(version.ProductVersion) ? "Unknown" : version.ProductVersion;
            if (!string.IsNullOrWhiteSpace(version.ProductName) &&
                !new[] { "renpy", "python", "unity", "nw.js" }.Any(x => version.ProductName.Contains(x, StringComparison.OrdinalIgnoreCase)))
                candidate.Name = version.ProductName;
            if (Directory.Exists(Path.Combine(folder, "renpy")) || Directory.Exists(Path.Combine(folder, name + "_Data")) ||
                File.Exists(Path.Combine(folder, "UnityPlayer.dll")) || File.Exists(Path.Combine(folder, "Game.ini")) ||
                Directory.Exists(Path.Combine(folder, "www")))
                candidate.Confidence = 90;
            string save = Path.Combine(folder, "game", "saves");
            if (Directory.Exists(save)) candidate.SavePath = save;
            else if (Directory.Exists(Path.Combine(folder, "saves"))) candidate.SavePath = Path.Combine(folder, "saves");
            string[] coverNames = { "cover.gif", "banner.gif", "header.gif", "cover.png", "cover.jpg", "banner.png", "banner.jpg", "header.jpg" };
            candidate.CoverPath = coverNames.Select(n => Path.Combine(folder, n)).FirstOrDefault(File.Exists) ?? "";
        }
        catch (Exception ex) { Debug.WriteLine(ex.Message); }
        return candidate;
    }

    private static void ReadSteam(List<ScannedExecutable> result, CancellationToken cancellation)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            string steam = key?.GetValue("SteamPath") as string ?? "";
            if (!Directory.Exists(steam)) return;
            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steam };
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                    libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
            foreach (string library in libraries)
            {
                string apps = Path.Combine(library, "steamapps");
                if (!Directory.Exists(apps)) continue;
                foreach (string manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var fields = Regex.Matches(File.ReadAllText(manifest), "\"([^\"]+)\"\\s*\"([^\"]*)\"")
                        .Cast<Match>().GroupBy(m => m.Groups[1].Value).ToDictionary(g => g.Key, g => g.First().Groups[2].Value);
                    if (!fields.TryGetValue("installdir", out string? dir)) continue;
                    string folder = Path.Combine(apps, "common", dir);
                    if (!PathSafety.Within(folder, Path.Combine(apps, "common"))) continue;
                    var candidate = ScanFolder(folder, cancellation).Where(x => !x.IsArchive).OrderByDescending(x => x.Confidence).FirstOrDefault();
                    if (candidate == null) continue;
                    candidate.Name = fields.GetValueOrDefault("name", candidate.Name);
                    candidate.Version = fields.GetValueOrDefault("buildid", "Unknown");
                    candidate.GamePath = folder;
                    candidate.Source = "Steam";
                    candidate.Confidence = 100;
                    string id = fields.GetValueOrDefault("appid", "");
                    candidate.InstallSource = "https://store.steampowered.com/app/" + id;
                    string cache = Path.Combine(steam, "appcache", "librarycache", id + "_library_600x900.jpg");
                    if (File.Exists(cache)) candidate.CoverPath = cache;
                    result.Add(candidate);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Debug.WriteLine(ex.Message); }
    }
    private static void ReadEpic(List<ScannedExecutable> result, CancellationToken cancellation)
    {
        string manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests)) return;
        foreach (string file in Directory.EnumerateFiles(manifests, "*.item"))
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                string Field(string name) => doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? "" : "";
                string folder = Field("InstallLocation");
                string exe = Path.Combine(folder, Field("LaunchExecutable"));
                if (!File.Exists(exe) || !PathSafety.Within(exe, folder)) continue;
                var candidate = Describe(exe);
                if (candidate == null) continue;
                candidate.Name = Field("DisplayName");
                candidate.GamePath = folder;
                candidate.Version = Field("AppVersionString");
                candidate.Source = "Epic Games";
                candidate.InstallSource = "https://store.epicgames.com/";
                candidate.Confidence = 100;
                result.Add(candidate);
            }
            catch (Exception ex) { Debug.WriteLine(ex.Message); }
        }
    }
    public void Dispose()
    {
        foreach (var watcher in watchers) watcher.Dispose();
        debounce.Dispose();
    }
}
