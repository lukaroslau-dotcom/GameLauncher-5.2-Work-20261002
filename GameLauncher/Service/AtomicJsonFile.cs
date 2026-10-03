using System.IO;
using System.Text.Json;

namespace GameLauncher.Service;

public static class AtomicJsonFile
{
    public static void Write<T>(string path, T value)
    {
        PathSafety.RejectLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".pending-" + Guid.NewGuid().ToString("N");
        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(output, value, new JsonSerializerOptions { WriteIndented = true }); output.Flush(true); }
        PathSafety.RejectLinks(path); PathSafety.RejectLinks(path + ".bak");
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
        else File.Move(temporary, path);
    }
}
