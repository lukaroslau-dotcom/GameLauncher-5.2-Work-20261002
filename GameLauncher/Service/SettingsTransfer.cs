using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using GameLauncher.models;

namespace GameLauncher.Service;

public static class SettingsTransfer
{
    public sealed record Preview(string SourcePath, string SourceHash, string Baseline, DateTime ExpiresUtc);
    public static void Export(AppSettings settings, string destination)
    {
        PathSafety.RejectLinks(destination);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(output, settings, new JsonSerializerOptions { WriteIndented = true }); output.Flush(true);
    }
    private static byte[] Read(string path)
    {
        PathSafety.RejectLinks(path); using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > 4 * 1024 * 1024) throw new IOException("Einstellungsdatei zu groß.");
        using var memory = new MemoryStream(); input.CopyTo(memory); return memory.ToArray();
    }
    public static Preview Inspect(string path, AppSettings current)
    {
        byte[] bytes = Read(path);
        _ = JsonSerializer.Deserialize<AppSettings>(bytes) ?? throw new IOException("Ungültige Einstellungen.");
        return new(Path.GetFullPath(path), Convert.ToHexString(SHA256.HashData(bytes)), JsonSerializer.Serialize(current), DateTime.UtcNow.AddMinutes(10));
    }
    public static AppSettings Apply(Preview preview, AppSettings current)
    {
        if (preview.ExpiresUtc < DateTime.UtcNow || preview.Baseline != JsonSerializer.Serialize(current)) throw new IOException("Vorschau ist abgelaufen oder die Einstellungen wurden geändert.");
        byte[] bytes = Read(preview.SourcePath);
        if (Convert.ToHexString(SHA256.HashData(bytes)) != preview.SourceHash) throw new IOException("Einstellungsdatei wurde seit der Vorschau verändert.");
        var proposed = JsonSerializer.Deserialize<AppSettings>(bytes) ?? throw new IOException("Ungültige Einstellungen.");
        // Machine-local authority and external side effects are never adopted from a settings file.
        proposed.ArchivePath = current.ArchivePath; proposed.ManagedGameFolder = current.ManagedGameFolder;
        proposed.DefaultGameFolder = current.DefaultGameFolder; proposed.DefaultSaveFolder = current.DefaultSaveFolder;
        proposed.BackgroundPath = current.BackgroundPath; proposed.IsFirstRun = current.IsFirstRun;
        proposed.StartWithWindows = current.StartWithWindows; proposed.LaunchShortcutEnabled = current.LaunchShortcutEnabled;
        proposed.LaunchShortcutKey = current.LaunchShortcutKey; proposed.KeyboardShortcutEnabled = current.KeyboardShortcutEnabled;
        proposed.GlobalMouseShortcutEnabled = current.GlobalMouseShortcutEnabled; proposed.RemoteHostAutoStart = current.RemoteHostAutoStart;
        proposed.ShortcutKey = current.ShortcutKey; proposed.ShortcutModifiers = current.ShortcutModifiers;
        proposed.RemoteHostPort = current.RemoteHostPort; proposed.RemoteHostFps = current.RemoteHostFps;
        proposed.RemoteHostQuality = current.RemoteHostQuality; proposed.RemoteHostWidth = current.RemoteHostWidth;
        proposed.RemotePrivacyCurtain = current.RemotePrivacyCurtain; proposed.RemotePerformanceMode = current.RemotePerformanceMode;
        proposed.AutoAddDetectedGames = current.AutoAddDetectedGames; proposed.ManageLocalGames = current.ManageLocalGames;
        proposed.UiScale = double.IsFinite(proposed.UiScale) ? Math.Clamp(proposed.UiScale, .8, 2) : 1;
        proposed.BackupIntervalMinutes = Math.Clamp(proposed.BackupIntervalMinutes, 0, 10080);
        proposed.F95UpdateCheckIntervalHours = Math.Clamp(proposed.F95UpdateCheckIntervalHours, 6, 168);
        return proposed;
    }
}
