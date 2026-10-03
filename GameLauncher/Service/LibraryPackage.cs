using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace GameLauncher.Service;

public static class LibraryPackage
{
    public sealed record FileReceipt(string Path, long Bytes, string Sha256);
    public static void Export(GameService service, string destination, CancellationToken cancellation = default)
    {
        string root = service.Paths.ArchivePath;
        destination = Path.GetFullPath(destination);
        PathSafety.RejectLinks(destination);
        if (PathSafety.Within(destination, root) || File.Exists(destination)) throw new IOException("Bitte eine neue Exportdatei außerhalb des Datenordners wählen.");

        var files = new List<string> { service.Paths.GamesFilePath };
        if (File.Exists(service.Paths.SettingsFilePath)) files.Add(service.Paths.SettingsFilePath);
        foreach (string relative in new[] { "GamesData/Covers", "GamesData/F95Galleries", "SaveArchive", "SaveHistory", "RetiredSaves" })
        {
            string folder = Path.Combine(root, relative);
            if (!Directory.Exists(folder)) continue;
            PathSafety.RejectTreeLinks(folder);
            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            { if (files.Count >= 100000) throw new IOException("Export enthält zu viele Dateien."); files.Add(file); }
        }
        long total = 0;
        foreach (string file in files) { PathSafety.RejectLinks(file); total = checked(total + new FileInfo(file).Length); }
        if (total > 20L * 1024 * 1024 * 1024) throw new IOException("Export überschreitet das 20-GB-Limit.");
        if (new DriveInfo(Path.GetPathRoot(destination)!).AvailableFreeSpace < total + 128L * 1024 * 1024) throw new IOException("Nicht genügend Speicherplatz für verifizierten Export.");
        string stage = destination + ".pending-" + Guid.NewGuid().ToString("N");
        var receipts = new List<FileReceipt>();
        try
        {
            using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
                {
                    byte[] buffer = new byte[128 * 1024]; long streamed = 0;
                    foreach (string file in files)
                    {
                        cancellation.ThrowIfCancellationRequested(); PathSafety.RejectLinks(file);
                        string name = Path.GetRelativePath(root, file).Replace('\\', '/');
                        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        using var entry = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
                        long bytes = 0; int read;
                        while ((read = input.Read(buffer)) > 0)
                        { cancellation.ThrowIfCancellationRequested(); bytes += read; streamed += read; if (streamed > total) throw new IOException("Exportquelle ist gewachsen."); hash.AppendData(buffer, 0, read); entry.Write(buffer, 0, read); }
                        receipts.Add(new(name, bytes, Convert.ToHexString(hash.GetHashAndReset())));
                    }
                    using var manifest = zip.CreateEntry("export-manifest.json").Open();
                    JsonSerializer.Serialize(manifest, new { format = 1, sourceRoot = root, createdUtc = DateTime.UtcNow, includes = "Library, media, save backups, settings; no installations or downloads", files = receipts });
                }
                output.Flush(true);
            }
            using (var zip = ZipFile.OpenRead(stage))
                foreach (var receipt in receipts)
                {
                    cancellation.ThrowIfCancellationRequested();
                    using var input = zip.GetEntry(receipt.Path)!.Open();
                    if (zip.GetEntry(receipt.Path)!.Length != receipt.Bytes || Convert.ToHexString(SHA256.HashData(input)) != receipt.Sha256) throw new IOException("Exportprüfung fehlgeschlagen.");
                }
            cancellation.ThrowIfCancellationRequested(); File.Move(stage, destination, false);
        }
        catch
        {
            // This unique partial file is ours; never remove the requested destination.
            try { if (File.Exists(stage)) { PathSafety.RejectLinks(stage); File.Delete(stage); } } catch { }
            throw;
        }
    }
}
