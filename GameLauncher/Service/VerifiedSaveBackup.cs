using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace GameLauncher.Service;

public static class VerifiedSaveBackup
{
    public const string ManifestName = ".gamelauncher-backup.json";
    public sealed record Entry(string Path, long Length, string Sha256);
    public sealed record Manifest(int Format, DateTime CreatedUtc, List<Entry> Files);

    public static List<Entry> Snapshot(string root, bool backup = false)
    {
        PathSafety.RejectTreeLinks(root);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Take(100001).ToArray();
        if (files.Length > 100000) throw new IOException("Zu viele Save-Dateien.");
        var result = new List<Entry>();
        foreach (string file in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            string relative = Path.GetRelativePath(root, file);
            if (backup && relative.Equals(ManifestName, StringComparison.OrdinalIgnoreCase)) continue;
            if (relative.Equals(ManifestName, StringComparison.OrdinalIgnoreCase)) throw new IOException("Reservierter Dateiname im Save-Ordner.");
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            result.Add(new(relative, stream.Length, Convert.ToHexString(SHA256.HashData(stream))));
        }
        return result;
    }

    public static void AssertEqual(IReadOnlyList<Entry> expected, IReadOnlyList<Entry> actual)
    {
        if (expected.Count != actual.Count || !expected.SequenceEqual(actual)) throw new IOException("SHA-256-Prüfung fehlgeschlagen; Dateien wurden verändert.");
    }

    public static void Create(string source, string stage)
    {
        var before = Snapshot(source);
        PathSafety.CopyTree(source, stage, false);
        AssertEqual(before, Snapshot(source));
        AssertEqual(before, Snapshot(stage));
        using (var output = new FileStream(Path.Combine(stage, ManifestName), FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(output, new Manifest(1, DateTime.UtcNow, before));
            output.Flush(true);
        }
        Verify(stage);
    }

    public static bool Verify(string source)
    {
        PathSafety.RejectTreeLinks(source);
        string manifestPath = Path.Combine(source, ManifestName);
        if (!File.Exists(manifestPath)) return false; // Historical 5.2 backup; never claim historical integrity.
        if (new FileInfo(manifestPath).Length > 32 * 1024 * 1024) throw new IOException("Backupmanifest zu groß.");
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath));
        if (manifest is not { Format: 1, Files: not null }) throw new IOException("Ungültiges Backupmanifest.");
        AssertEqual(manifest.Files, Snapshot(source, true));
        return true;
    }

    public static void CopyPayload(string source, string stage)
    {
        Verify(source);
        var expected = Snapshot(source, true);
        PathSafety.CopyTree(source, stage);
        string metadata = Path.Combine(stage, ManifestName);
        if (File.Exists(metadata)) File.Delete(metadata);
        // Merge may contain additional files; every restored source file must still match.
        foreach (var entry in expected)
        {
            string file = Path.Combine(stage, entry.Path);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != entry.Length || Convert.ToHexString(SHA256.HashData(stream)) != entry.Sha256)
                throw new IOException("Wiederherstellung hat die SHA-256-Prüfung nicht bestanden.");
        }
        AssertEqual(expected, Snapshot(source, true));
    }

    public static void RetireOlder(string history, string retired)
    {
        if (!Directory.Exists(history)) return;
        PathSafety.RejectTreeLinks(history);
        var versions = new DirectoryInfo(history).GetDirectories().OrderByDescending(d => d.CreationTimeUtc).ToArray();
        foreach (var version in versions.Skip(1))
        {
            PathSafety.RejectLinks(retired);
            Directory.CreateDirectory(retired);
            Directory.Move(version.FullName, Path.Combine(retired, version.Name));
        }
    }
}
