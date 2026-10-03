using System.IO;
using System.Text.Json;
using GameLauncher.models;

namespace GameLauncher.Service;

public static class InstallationRecovery
{
    public sealed record Receipt(int Format, Guid Id, string GameId, string Name, string Original, string Stored,
        string Executable, string Save, DateTime RemovedUtc, List<VerifiedSaveBackup.Entry> Files);

    public static void Remove(AppPaths paths, Game game)
    {
        string source = PathSafety.Full(game.GamePath);
        Guid id = Guid.NewGuid();
        string stored = source + ".gl-removed-" + id.ToString("N");
        var files = VerifiedSaveBackup.Snapshot(source);
        var receipt = new Receipt(1, id, game.Id, game.Name, source, stored, game.ExecutablePath, game.SavePath, DateTime.UtcNow, files);
        // Durable intent before same-volume atomic rename. A crash on either side is inspectable.
        AtomicJsonFile.Write(ReceiptPath(paths, id), receipt);
        PathSafety.RejectTreeLinks(source); PathSafety.RejectLinks(stored);
        VerifiedSaveBackup.AssertEqual(files, VerifiedSaveBackup.Snapshot(source));
        Directory.Move(source, stored);
    }

    public static List<Receipt> List(AppPaths paths)
    {
        string root = Path.Combine(paths.ArchivePath, "RemovedInstallations");
        if (!Directory.Exists(root)) return new();
        PathSafety.RejectLinks(root);
        var result = new List<Receipt>();
        foreach (string file in Directory.EnumerateFiles(root, "*.json").Take(1000))
        {
            try
            {
                if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out Guid id)) continue;
                var receipt = Read(paths, id);
                if (Directory.Exists(receipt.Stored)) result.Add(receipt);
            }
            catch (Exception ex) { EventLog.Write("recovery-read", "Recovery record remains unchanged", ex); }
        }
        return result.OrderByDescending(r => r.RemovedUtc).ToList();
    }

    public static Receipt Restore(AppPaths paths, Guid id)
    {
        var receipt = Read(paths, id);
        if (File.Exists(receipt.Original) || Directory.Exists(receipt.Original)) throw new IOException("Am ursprünglichen Ort liegt bereits eine Datei oder Installation. Nichts wurde überschrieben.");
        PathSafety.RejectLinks(receipt.Original);
        if (!paths.CanSafelyDeleteGamePath(receipt.Stored)) throw new IOException("Wiederherstellungsordner ist nicht sicher erreichbar.");
        VerifiedSaveBackup.AssertEqual(receipt.Files, VerifiedSaveBackup.Snapshot(receipt.Stored));
        Directory.Move(receipt.Stored, receipt.Original);
        return receipt;
    }

    private static Receipt Read(AppPaths paths, Guid id)
    {
        string file = ReceiptPath(paths, id); PathSafety.RejectLinks(file);
        if (new FileInfo(file).Length > 32 * 1024 * 1024) throw new IOException("Wiederherstellungsnachweis zu groß.");
        var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(file));
        if (receipt == null || receipt.Format != 1 || receipt.Id != id || receipt.Files == null ||
            !PathSafety.Same(receipt.Stored, PathSafety.Full(receipt.Original) + ".gl-removed-" + id.ToString("N")) ||
            !PathSafety.Within(receipt.Executable, receipt.Original)) throw new IOException("Ungültiger Wiederherstellungsnachweis.");
        return receipt;
    }
    private static string ReceiptPath(AppPaths paths, Guid id) => Path.Combine(paths.ArchivePath, "RemovedInstallations", id.ToString("N") + ".json");
}
