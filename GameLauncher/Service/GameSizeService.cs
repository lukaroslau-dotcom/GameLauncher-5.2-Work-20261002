using System.Globalization;
using System.IO;

namespace GameLauncher.Service;

public static class GameSizeService
{
    private static readonly SemaphoreSlim scans = new(2, 2);

    public static async Task<long?> CalculateAsync(string? gamePath, CancellationToken ct = default)
    {
        await scans.WaitAsync(ct).ConfigureAwait(false);
        try { return await Task.Run(() => Calculate(gamePath, ct), ct).ConfigureAwait(false); }
        finally { scans.Release(); }
    }

    private static long? Calculate(string? gamePath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(gamePath)) return null;
        try
        {
            var pending = new Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(gamePath));
            long bytes = 0;
            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var folder = pending.Pop();
                if (!folder.Exists || (folder.Attributes & FileAttributes.ReparsePoint) != 0) return null;
                foreach (var entry in folder.EnumerateFileSystemInfos())
                {
                    ct.ThrowIfCancellationRequested();
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) return null;
                    if (entry is DirectoryInfo directory) pending.Push(directory);
                    else if (entry is FileInfo file) bytes = checked(bytes + file.Length);
                }
            }
            return bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException or OverflowException)
        {
            return null;
        }
    }

    public static string Format(long? bytes)
    {
        if (bytes is null or < 0) return "Größe unbekannt";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes.Value;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return value.ToString(unit == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + units[unit];
    }
}
