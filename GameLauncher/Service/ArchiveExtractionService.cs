using System.IO;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace GameLauncher.Service;

public sealed record ArchiveExtractionResult(bool Success, string Destination, string Tool, bool SourceDeleted, string Error);

public static class ArchiveExtractionService
{
    public const long MaxExpandedBytes = 20L * 1024 * 1024 * 1024;
    public const int MaxEntries = 100000;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z" };
    public static bool IsSupported(string? path) => !string.IsNullOrWhiteSpace(path) && Extensions.Contains(Path.GetExtension(path));

    // Retained API for old callers; originals are now always preserved.
    public static ArchiveExtractionResult ExtractAndDelete(string archivePath, string gameRoot, CancellationToken cancellation, bool preferExternal = true)
        => Extract(archivePath, gameRoot, cancellation);

    public static ArchiveExtractionResult Extract(string archivePath, string gameRoot, CancellationToken cancellation, long maxBytes = MaxExpandedBytes)
    {
        string? stage = null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (maxBytes < 1 || maxBytes > MaxExpandedBytes) throw new IOException("Ungültiges Entpacklimit.");
            string source = Path.GetFullPath(archivePath);
            string root = Path.GetFullPath(gameRoot);
            if (!IsSupported(source)) throw new IOException("Unterstützt werden ZIP, RAR und 7z.");
            PathSafety.RejectLinks(source);
            PathSafety.RejectLinks(root);
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("Archivverknüpfungen sind nicht erlaubt.");
            Directory.CreateDirectory(root);
            string name = Path.GetFileNameWithoutExtension(source).Trim().TrimEnd('.');
            if (name.Length == 0) name = "Entpacktes Spiel";
            string destination = Path.Combine(root, name);
            for (int n = 2; Directory.Exists(destination) || File.Exists(destination); n++) destination = Path.Combine(root, $"{name} ({n})");
            stage = Path.Combine(root, ".entpacken-" + Guid.NewGuid().ToString("N"));
            using var heldSource = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = ArchiveFactory.OpenArchive(heldSource);
            if (archive.Type is not (ArchiveType.Zip or ArchiveType.Rar or ArchiveType.SevenZip)) throw new IOException("Nicht unterstütztes Archivformat.");
            var entries = archive.Entries.Take(MaxEntries + 1).ToArray();
            if (entries.Length == 0 || entries.Length > MaxEntries) throw new IOException("Leeres Archiv oder zu viele Einträge.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in entries)
            {
                cancellation.ThrowIfCancellationRequested();
                string target = Child(stage, entry.Key ?? "");
                if (!seen.Add(target)) throw new IOException("Doppelter Archivpfad.");
                if (entry.IsEncrypted || !string.IsNullOrEmpty(entry.LinkTarget)) throw new IOException("Verschlüsselte Einträge und Verknüpfungen werden abgelehnt.");
                if (entry.Attrib is int attributes && ((attributes & 0x400) != 0 || ((attributes >> 16) & 0xF000) is not (0 or 0x8000 or 0x4000)))
                    throw new IOException("Archiv enthält eine Spezialdatei.");
                total = checked(total + entry.Size);
                if (entry.Size < 0 || total > maxBytes) throw new IOException("Entpacklimit überschritten.");
            }
            if (new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace < total + 128L * 1024 * 1024) throw new IOException("Nicht genügend freier Speicherplatz.");
            Directory.CreateDirectory(stage);
            long written = 0;
            byte[] buffer = new byte[128 * 1024];
            foreach (var entry in entries)
            {
                cancellation.ThrowIfCancellationRequested();
                string target = Child(stage, entry.Key!);
                if (entry.IsDirectory) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                PathSafety.RejectLinks(target);
                using var input = entry.OpenEntryStream();
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                long entryWritten = 0;
                int count;
                while ((count = input.Read(buffer)) > 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    written = checked(written + count); entryWritten = checked(entryWritten + count);
                    if (written > maxBytes || entryWritten > entry.Size) throw new IOException("Tatsächliche Entpackgröße überschreitet das Limit.");
                    output.Write(buffer, 0, count);
                }
                if (entryWritten != entry.Size) throw new IOException("Unvollständiger Archiveintrag.");
                output.Flush(true);
            }
            cancellation.ThrowIfCancellationRequested();
            PathSafety.RejectTreeLinks(stage);
            var children = new DirectoryInfo(stage).GetFileSystemInfos();
            string payload = children.Length == 1 && children[0] is DirectoryInfo ? children[0].FullName : stage;
            Directory.Move(payload, destination);
            if (Directory.Exists(stage)) Directory.Delete(stage);
            return new(true, destination, "ZIP/RAR/7z", false, "");
        }
        catch (Exception ex)
        {
            string cleanup = "";
            if (stage != null && Directory.Exists(stage))
            {
                try { PathSafety.RejectTreeLinks(stage); Directory.Delete(stage, true); }
                catch { cleanup = " Unvollständiger Arbeitsordner bleibt erhalten: " + stage; }
            }
            return new(false, "", "ZIP/RAR/7z", false,
                (ex is OperationCanceledException ? "Entpacken abgebrochen." : "Entpacken fehlgeschlagen: " + ex.Message) + " Originalarchiv bleibt erhalten." + cleanup);
        }
    }

    private static string Child(string root, string key)
    {
        string relative = key.Replace('\\', '/').TrimEnd('/');
        string[] parts = relative.Split('/');
        if (parts.Length > 64 || parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') ||
            p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || IsDevice(p))) throw new IOException("Unsicherer Windows-Archivpfad.");
        string target = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (target.Length > 240 || !PathSafety.Within(target, root) || PathSafety.Same(target, root)) throw new IOException("Unsicherer oder zu langer Archivpfad.");
        return target;
    }
    private static bool IsDevice(string part)
    {
        string stem = part.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789¹²³".Contains(stem[3]));
    }
}
