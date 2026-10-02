using System.IO;

namespace GameLauncher.Service;

public static class PathSafety
{
    public static string Full(string path) => Path.GetFullPath(path)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static bool Same(string a, string b) =>
        string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);

    public static bool Within(string path, string parent) => Same(path, parent) ||
        Full(path).StartsWith(Full(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static bool Overlaps(string a, string b) => Within(a, b) || Within(b, a);

    public static void RejectLinks(string path)
    {
        for (DirectoryInfo? directory = new DirectoryInfo(Full(path)); directory != null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Verknuepfte Ordner werden fuer Dateioperationen nicht unterstuetzt.");
    }

    public static void CopyTree(string source, string destination, bool overwrite = true)
    {
        RejectLinks(source);
        RejectLinks(destination);
        if (Overlaps(source, destination))
            throw new IOException("Quell- und Zielordner duerfen sich nicht ueberschneiden.");
        CopyContents(source, destination, overwrite);
    }

    private static void CopyContents(string source, string destination, bool overwrite)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Der Ordner enthaelt eine Verknuepfung: " + entry.Name);
            string target = Path.Combine(destination, entry.Name);
            if (entry is DirectoryInfo)
                CopyContents(entry.FullName, target, overwrite);
            else
                File.Copy(entry.FullName, target, overwrite);
        }
    }

    public static void RejectTreeLinks(string path)
    {
        RejectLinks(path);
        foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Der Ordner enthaelt eine Verknuepfung.");
            if (entry is DirectoryInfo) RejectTreeLinks(entry.FullName);
        }
    }
}
