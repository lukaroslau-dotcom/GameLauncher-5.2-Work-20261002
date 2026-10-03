using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using GameLauncher.models;
using GameLauncher.Service;

internal static class MigrationChecks
{
    public static void Run(string source)
    {
        byte[] original = File.ReadAllBytes(source);
        string root = Path.Combine(Path.GetTempPath(), "GameLauncher.52.Migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string copy = Path.Combine(root, "source-copy.json"); File.WriteAllBytes(copy, original);
        var entries = JsonSerializer.Deserialize<List<Game>>(original) ?? throw new IOException("Invalid source");
        var service = new GameService(new AppPaths(Path.Combine(root, "Imported")));
        var importer = new ImportService(); var preview = importer.Preview(copy, service);
        var first = importer.ImportGamesJson(copy, service, preview.Sha256);
        if (first.error != null || first.added + first.skipped != entries.Count) throw new IOException("Import count mismatch: " + first.error);
        var persisted = new GameService(service.Paths);
        foreach (var entry in entries.Where(e => !string.IsNullOrEmpty(e.Id)))
        {
            var saved = persisted.ShowGame(entry.Id);
            // A duplicate executable may be deliberately skipped; record count instead of claiming identity for it.
            if (saved == null) { if (first.skipped > 0) continue; throw new IOException("Missing original ID"); }
            if (saved.Name != entry.Name || saved.Playtime != entry.Playtime || saved.IsFavorite != entry.IsFavorite || saved.CoverPath != entry.CoverPath || saved.SavePath != entry.SavePath || saved.ExecutablePath != entry.ExecutablePath)
                throw new IOException("Imported fields differ");
        }
        var second = importer.ImportGamesJson(copy, persisted);
        if (second.error != null || second.added != 0 || second.skipped != entries.Count) throw new IOException("Repeat import is not idempotent");
        if (!File.ReadAllBytes(source).SequenceEqual(original)) throw new IOException("Original source changed");
        var receipt = new { passed = true, sourceSha256 = Convert.ToHexString(SHA256.HashData(original)), sourceCount = entries.Count, added = first.added, skipped = first.skipped, missingInstallations = preview.MissingInstallations, repeatedAdded = second.added, originalUnchanged = true, root };
        string json = JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "migration-result.json"), json); Console.WriteLine(json);
    }
}
