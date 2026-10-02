using GameLauncher.models;
using System.IO;

namespace GameLauncher.Service;

public sealed record GameStorageMoveResult(bool Success, string GamePath, string ExecutablePath, string SavePath, string CoverPath, bool SourceRemoved, string Error);

public static class GameStorageService
{
    public static bool IsPlatformManaged(Game game) => IsPlatformManaged(game.InstallSource);
    public static bool IsPlatformManaged(string? source) => new[] { "steam", "epic games", "microsoft store", "xbox" }
        .Any(platform => (source ?? "").Contains(platform, StringComparison.OrdinalIgnoreCase));

    public static GameStorageMoveResult MoveToLibrary(string gameFolder, string executablePath, string savePath, string coverPath,
        string libraryRoot, CancellationToken cancellation)
    {
        string source;
        string root;
        try
        {
            source = PathSafety.Full(gameFolder);
            root = PathSafety.Full(libraryRoot);
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException("Der Spielordner wurde nicht gefunden.");
            if (!File.Exists(executablePath) || !PathSafety.Within(executablePath, source)) throw new IOException("Die Startdatei liegt nicht im Spielordner.");
            RejectProtectedFolder(source);
            Directory.CreateDirectory(root);
            PathSafety.RejectTreeLinks(source);
            PathSafety.RejectLinks(root);
            if (PathSafety.Within(source, root)) return new(true, source, executablePath, savePath, coverPath, true, "");
            if (PathSafety.Overlaps(source, root)) throw new IOException("Spielordner und GameLibrary dürfen sich nicht überschneiden.");
        }
        catch (Exception ex) { return new(false, gameFolder, executablePath, savePath, coverPath, false, ex.Message); }

        string name = SafeFolderName(Path.GetFileName(source));
        string destination = UniqueDestination(root, name);
        string stage = destination + ".übernehmen-" + Guid.NewGuid().ToString("N");
        try
        {
            cancellation.ThrowIfCancellationRequested();
            bool sameDrive = string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase);
            if (sameDrive)
            {
                Directory.Move(source, destination);
            }
            else
            {
                CopyTree(source, stage, cancellation);
                var original = TreeStats(source, cancellation);
                var copy = TreeStats(stage, cancellation);
                if (original != copy) throw new IOException("Die kopierten Spieldateien konnten nicht vollständig geprüft werden.");
                Directory.Move(stage, destination);
                try { PathSafety.RejectTreeLinks(source); Directory.Delete(source, true); }
                catch (Exception ex)
                {
                    return Map(true, source, destination, executablePath, savePath, coverPath, false,
                        "Das Spiel wurde übernommen, der alte Ordner konnte aber nicht gelöscht werden: " + ex.Message);
                }
            }
            return Map(true, source, destination, executablePath, savePath, coverPath, true, "");
        }
        catch (OperationCanceledException)
        {
            CleanupStage(stage);
            return new(false, gameFolder, executablePath, savePath, coverPath, false, "Übernahme abgebrochen; der ursprüngliche Spielordner bleibt erhalten.");
        }
        catch (Exception ex)
        {
            CleanupStage(stage);
            return new(false, gameFolder, executablePath, savePath, coverPath, false, "Spiel konnte nicht in die GameLibrary übernommen werden: " + ex.Message);
        }
    }

    private static GameStorageMoveResult Map(bool success, string source, string destination, string executable, string save, string cover, bool removed, string error)
    {
        string Relocate(string value) => !string.IsNullOrWhiteSpace(value) && PathSafety.Within(value, source)
            ? Path.Combine(destination, Path.GetRelativePath(source, value)) : value;
        return new(success, destination, Relocate(executable), Relocate(save), Relocate(cover), removed, error);
    }

    private static void CopyTree(string source, string destination, CancellationToken cancellation)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            cancellation.ThrowIfCancellationRequested();
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Der Spielordner enthält eine nicht unterstützte Verknüpfung.");
            string target = Path.Combine(destination, entry.Name);
            if (entry is DirectoryInfo directory) CopyTree(directory.FullName, target, cancellation);
            else
            {
                using var input = new FileStream(entry.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan);
                input.CopyTo(output, 1024 * 1024);
            }
        }
    }

    private static (long Files,long Bytes) TreeStats(string path, CancellationToken cancellation)
    {
        long files=0,bytes=0;
        foreach(string file in Directory.EnumerateFiles(path,"*",SearchOption.AllDirectories))
        {
            cancellation.ThrowIfCancellationRequested(); files++; bytes=checked(bytes+new FileInfo(file).Length);
        }
        return(files,bytes);
    }

    private static string SafeFolderName(string name)
    {
        foreach(char invalid in Path.GetInvalidFileNameChars()) name=name.Replace(invalid,'_');
        name=name.Trim().TrimEnd('.'); return name.Length==0?"Spiel":name;
    }

    private static void RejectProtectedFolder(string source)
    {
        var protectedFolders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Path.GetPathRoot(source) ?? ""
        };
        if (protectedFolders.Where(path => !string.IsNullOrWhiteSpace(path)).Any(path => PathSafety.Same(path, source)))
            throw new IOException("Bitte den eigentlichen Spielordner auswählen, nicht einen kompletten Windows- oder Benutzerordner.");
    }
    private static string UniqueDestination(string root,string name)
    {
        string candidate=Path.Combine(root,name);
        for(int index=2;Directory.Exists(candidate)||File.Exists(candidate);index++) candidate=Path.Combine(root,$"{name} ({index})");
        return candidate;
    }
    private static void CleanupStage(string stage)
    {
        try
        {
            if(Directory.Exists(stage)&&Path.GetFileName(stage).Contains(".übernehmen-",StringComparison.Ordinal))
            {PathSafety.RejectTreeLinks(stage);Directory.Delete(stage,true);}
        }
        catch { }
    }
}
