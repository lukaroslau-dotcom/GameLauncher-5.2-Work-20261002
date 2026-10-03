using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using GameLauncher.models;
namespace GameLauncher.Service;
public static class LibraryPackageImport
{
    public static string ImportToNewFolder(string package,string parent,CancellationToken cancellation=default)
    {
        // Managed extraction publishes a unique folder and never replaces an existing destination.
        var extracted=ArchiveExtractionService.Extract(package,parent,cancellation);
        if(!extracted.Success)throw new IOException(extracted.Error);
        string root=extracted.Destination;
        try
        {
            string manifestFile=Path.Combine(root,"export-manifest.json");PathSafety.RejectLinks(manifestFile);
            if(!File.Exists(manifestFile)||new FileInfo(manifestFile).Length>32*1024*1024)throw new IOException("Kein gültiges Bibliotheksmanifest.");
            using var document=JsonDocument.Parse(File.ReadAllBytes(manifestFile));var manifest=document.RootElement;
            if(manifest.GetProperty("format").GetInt32()!=1)throw new IOException("Unbekanntes Paketformat.");
            var receipts=manifest.GetProperty("files").Deserialize<List<LibraryPackage.FileReceipt>>()??throw new IOException("Dateiliste fehlt.");
            if(receipts.Count>100000)throw new IOException("Zu viele Dateien.");
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var receipt in receipts)
            {
                cancellation.ThrowIfCancellationRequested();string relative=receipt.Path.Replace('/',Path.DirectorySeparatorChar);string file=Path.GetFullPath(Path.Combine(root,relative));
                if(Path.IsPathRooted(relative)||!PathSafety.Within(file,root)||!names.Add(Path.GetRelativePath(root,file)))throw new IOException("Ungültiger Manifestpfad.");
                PathSafety.RejectLinks(file);
                using var input=File.OpenRead(file);if(input.Length!=receipt.Bytes||!Convert.ToHexString(SHA256.HashData(input)).Equals(receipt.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Paketdatei stimmt nicht mit Prüfsumme überein.");
            }
            var actual=Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Select(p=>Path.GetRelativePath(root,p)).Where(p=>!p.Equals("export-manifest.json",StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(!actual.SetEquals(names))throw new IOException("Paket enthält nicht deklarierte Dateien.");
            var paths=new AppPaths(root);
            var games=JsonSerializer.Deserialize<List<Game>>(File.ReadAllBytes(paths.GamesFilePath))??throw new IOException("Bibliothek fehlt.");
            if(games.Count>100000||games.Any(g=>g==null))throw new IOException("Ungültige Spieleliste.");
            string sourceRoot=manifest.TryGetProperty("sourceRoot",out var oldRoot)?oldRoot.GetString()??"":"";
            string Remap(string value)
            {
                if(string.IsNullOrWhiteSpace(value))return "";
                if(sourceRoot.Length>0&&PathSafety.Within(value,sourceRoot))
                {string candidate=Path.GetFullPath(Path.Combine(root,Path.GetRelativePath(sourceRoot,value)));if(File.Exists(candidate))return candidate;}
                return value;
            }
            foreach(var game in games){GameService.NormalizeGame(game);game.CoverPath=Remap(game.CoverPath);game.GalleryPaths=game.GalleryPaths.Select(Remap).ToList();}
            if(games.Select(g=>g.Id).Distinct().Count()!=games.Count)throw new IOException("Doppelte Spielidentitäten.");
            cancellation.ThrowIfCancellationRequested();
            File.Copy(paths.GamesFilePath,Path.Combine(root,"original-games.json"),false);
            AtomicJsonFile.Write(paths.GamesFilePath,games);
            File.Copy(paths.GamesFilePath, paths.GamesFilePath+".bak", true);
            if(File.Exists(paths.SettingsFilePath))File.Move(paths.SettingsFilePath,Path.Combine(root,"imported-settings.json"));
            // Imported machine authority is deliberately not activated. Source settings remain available for reviewed transfer.
            AtomicJsonFile.Write(paths.SettingsFilePath,new AppSettings {ArchivePath=root,ManagedGameFolder=Path.Combine(root,"GameLibrary"),AutoDiscoveryEnabled=false,AutoAddDetectedGames=false,F95UpdateChecksEnabled=false,KeyboardShortcutEnabled=false,RemoteHostAutoStart=false,StartWithWindows=false,IsFirstRun=false});
            return root;
        }
        catch(Exception ex)
        {
            // Preserve the extracted copy for inspection; never touch the input package or another library.
            throw new IOException("Import nicht aktiviert. Prüfkopie bleibt unter "+root+". "+ex.Message,ex);
        }
    }
}
