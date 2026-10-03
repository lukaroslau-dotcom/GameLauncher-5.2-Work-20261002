using System.IO;
using System.Text.Json;

namespace GameLauncher.Service;

public static class EventLog
{
    private static readonly object Gate = new();
    public static void Write(string operation, string message, Exception? error = null)
    {
        try
        {
            lock (Gate)
            {
                string root = Path.Combine(AppPaths.Default().ArchivePath, "Logs");
                PathSafety.RejectLinks(root); Directory.CreateDirectory(root);
                string file = Path.Combine(root, "events.jsonl"); PathSafety.RejectLinks(file);
                if (File.Exists(file) && new FileInfo(file).Length >= 2 * 1024 * 1024)
                    File.Move(file, Path.Combine(root, "events-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N") + ".jsonl"));
                File.AppendAllText(file, JsonSerializer.Serialize(new { at = DateTime.UtcNow, operation, message, error = error?.ToString() }) + Environment.NewLine);
            }
        }
        catch { /* Diagnostics must not replace the original exception. */ }
    }
}
