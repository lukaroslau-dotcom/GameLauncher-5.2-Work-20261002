using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace GameLauncher.Service;

public sealed record ArchiveExtractionResult(bool Success, string Destination, string Tool, bool SourceDeleted, string Error);

public static class ArchiveExtractionService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z" };

    public static bool IsSupported(string? path) => !string.IsNullOrWhiteSpace(path) && SupportedExtensions.Contains(Path.GetExtension(path));

    public static ArchiveExtractionResult ExtractAndDelete(string archivePath, string gameRoot, CancellationToken cancellation, bool preferExternal = true)
    {
        string source = Path.GetFullPath(archivePath);
        if (!File.Exists(source) || !IsSupported(source))
            return new(false, "", "", false, "Die Archivdatei wird nicht unterstützt oder wurde nicht gefunden.");

        string root;
        try
        {
            root = Path.GetFullPath(gameRoot);
            Directory.CreateDirectory(root);
            PathSafety.RejectLinks(root);
        }
        catch (Exception ex) { return new(false, "", "", false, "Der Spieleordner ist nicht verwendbar: " + ex.Message); }

        string destination = UniqueDestination(root, SafeFolderName(Path.GetFileNameWithoutExtension(source)));
        string stage = destination + ".entpacken-" + Guid.NewGuid().ToString("N");
        string tool = "Windows";
        try
        {
            cancellation.ThrowIfCancellationRequested();
            Directory.CreateDirectory(stage);
            string extension = Path.GetExtension(source);
            string? winRar = preferExternal ? FindExecutable("WinRAR.exe", "UnRAR.exe") : null;
            string? sevenZip = preferExternal ? FindExecutable("7z.exe", "7zz.exe") : null;

            if (winRar != null)
            {
                tool = Path.GetFileName(winRar).StartsWith("WinRAR", StringComparison.OrdinalIgnoreCase) ? "WinRAR" : "UnRAR";
                RunExtractor(winRar, tool == "WinRAR"
                    ? ["x", "-inul", "-o+", "-y", source, stage + Path.DirectorySeparatorChar]
                    : ["x", "-idq", "-o+", "-y", source, stage + Path.DirectorySeparatorChar], cancellation);
            }
            else if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ExtractZipSafely(source, stage, cancellation);
            }
            else if (sevenZip != null)
            {
                tool = "7-Zip";
                RunExtractor(sevenZip, ["x", "-y", "-aoa", "-o" + stage, source], cancellation);
            }
            else
            {
                throw new IOException("Für RAR- und 7z-Dateien wird WinRAR oder 7-Zip benötigt. ZIP-Dateien kann Windows direkt entpacken.");
            }

            cancellation.ThrowIfCancellationRequested();
            PathSafety.RejectTreeLinks(stage);
            var entries = new DirectoryInfo(stage).EnumerateFileSystemInfos().ToList();
            if (entries.Count == 0) throw new IOException("Das Archiv enthält keine Dateien.");

            string payload = entries.Count == 1 && entries[0] is DirectoryInfo onlyFolder ? onlyFolder.FullName : stage;
            Directory.Move(payload, destination);
            if (!PathSafety.Same(payload, stage) && Directory.Exists(stage)) Directory.Delete(stage);

            try
            {
                File.Delete(source);
                return new(true, destination, tool, true, "");
            }
            catch (Exception ex)
            {
                return new(true, destination, tool, false, "Entpackt, aber die Archivdatei konnte nicht gelöscht werden: " + ex.Message);
            }
        }
        catch (OperationCanceledException)
        {
            CleanupStage(stage);
            return new(false, "", tool, false, "Entpacken abgebrochen. Die Archivdatei wurde behalten.");
        }
        catch (Exception ex)
        {
            CleanupStage(stage);
            return new(false, "", tool, false, "Entpacken fehlgeschlagen. Die Archivdatei wurde behalten: " + ex.Message);
        }
    }

    private static void ExtractZipSafely(string source, string stage, CancellationToken cancellation)
    {
        using var zip = ZipFile.OpenRead(source);
        long required = 0;
        foreach (var entry in zip.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000) throw new IOException("Das ZIP enthält eine nicht erlaubte Verknüpfung.");
            required = checked(required + entry.Length);
            string target = Path.GetFullPath(Path.Combine(stage, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!PathSafety.Within(target, stage)) throw new IOException("Das ZIP enthält einen unsicheren Pfad.");
        }
        string root = Path.GetPathRoot(stage)!;
        if (new DriveInfo(root).AvailableFreeSpace < required + 128L * 1024 * 1024)
            throw new IOException("Auf dem Ziellaufwerk ist nicht genug freier Speicherplatz.");

        foreach (var entry in zip.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            string target = Path.GetFullPath(Path.Combine(stage, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, FileOptions.SequentialScan);
            input.CopyTo(output, 1024 * 128);
        }
    }

    private static void RunExtractor(string executable, IReadOnlyList<string> arguments, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Das Entpackprogramm konnte nicht gestartet werden.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        try
        {
            while (!process.WaitForExit(200)) cancellation.ThrowIfCancellationRequested();
        }
        catch
        {
            try { process.Kill(true); } catch { }
            throw;
        }
        Task.WaitAll(output, error);
        if (process.ExitCode != 0)
            throw new IOException($"{Path.GetFileName(executable)} meldet Fehlercode {process.ExitCode}: " + error.Result.Trim());
    }

    private static string? FindExecutable(params string[] names)
    {
        var folders = new List<string>();
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (programFiles.Length > 0) folders.AddRange([Path.Combine(programFiles, "WinRAR"), Path.Combine(programFiles, "7-Zip")]);
        if (programFilesX86.Length > 0) folders.AddRange([Path.Combine(programFilesX86, "WinRAR"), Path.Combine(programFilesX86, "7-Zip")]);
        folders.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        foreach (string folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (string name in names)
            {
                try { string candidate = Path.Combine(folder.Trim().Trim('"'), name); if (File.Exists(candidate)) return candidate; } catch { }
            }
        return null;
    }

    private static string SafeFolderName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        name = name.Trim().TrimEnd('.');
        return name.Length == 0 ? "Entpacktes Spiel" : name;
    }

    private static string UniqueDestination(string root, string name)
    {
        string candidate = Path.Combine(root, name);
        for (int index = 2; Directory.Exists(candidate) || File.Exists(candidate); index++) candidate = Path.Combine(root, $"{name} ({index})");
        return candidate;
    }

    private static void CleanupStage(string stage)
    {
        try
        {
            if (Directory.Exists(stage) && Path.GetFileName(stage).Contains(".entpacken-", StringComparison.Ordinal))
            {
                PathSafety.RejectTreeLinks(stage);
                Directory.Delete(stage, true);
            }
        }
        catch { }
    }
}
