using System.IO;
using System.Text.RegularExpressions;
namespace GameLauncher.Service;
public sealed record SaveLocation(string Path,string Reason);
public static class SaveLocationService
{
    public static SaveLocation? FindBest(string gameFolder,string name,CancellationToken cancellation)
    {
        var candidates=Find(gameFolder,name,cancellation);
        if(candidates.Count==0)return null;
        // Engine-declared and in-installation paths are deterministic. A name-only
        // match is useful as a suggestion, but should never silently win.
        var deterministic=candidates.FirstOrDefault(candidate =>
            candidate.Reason.StartsWith("Ren’Py:",StringComparison.Ordinal) ||
            candidate.Reason.StartsWith("Spielstandordner innerhalb",StringComparison.Ordinal));
        return deterministic??(candidates.Count==1?candidates[0]:null);
    }

    public static string SuggestedBrowseFolder(string gameFolder)
    {
        if(Directory.Exists(gameFolder))return Path.GetFullPath(gameFolder);
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    public static List<SaveLocation> Find(string gameFolder,string name,CancellationToken cancellation)
    {
        var found=new Dictionary<string,SaveLocation>(StringComparer.OrdinalIgnoreCase);
        void Add(string path,string reason)
        {
            if (!Directory.Exists(path)) return;
            path = System.IO.Path.GetFullPath(path);
            if (!string.IsNullOrWhiteSpace(gameFolder) && PathSafety.Same(path,gameFolder)) return;
            found.TryAdd(path,new(path,reason));
        }
        if(Directory.Exists(gameFolder))
        {
            foreach(var relative in new[]{"game/saves","saves","save","www/save","Saved/SaveGames"})Add(System.IO.Path.Combine(gameFolder,relative),"Spielstandordner innerhalb der Installation");
            string scripts=System.IO.Path.Combine(gameFolder,"game");
            if(Directory.Exists(scripts))
                foreach(string script in SafeFiles(scripts).Where(f=>f.EndsWith(".rpy",StringComparison.OrdinalIgnoreCase)).Take(60))
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        if(new FileInfo(script).Length>1024*1024)continue;
                        var match=Regex.Match(File.ReadAllText(script),"config\\.save_directory\\s*=\\s*[\"']([^\"']+)");
                        if(match.Success && !match.Groups[1].Value.Contains(".."))
                        {
                            string root=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"RenPy");
                            string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(root,match.Groups[1].Value));
                            if(PathSafety.Within(path,root))Add(path,"Ren’Py: in der Spielkonfiguration angegeben");
                        }
                    }catch(IOException){}catch(UnauthorizedAccessException){}
                }
        }
        static string Key(string value)=>Regex.Replace(value.ToLowerInvariant(),"[^a-z0-9]","");
        string wanted=Key(name);
        string[] nameParts=Regex.Split(name.ToLowerInvariant(),"[^a-z0-9]+")
            .Where(part=>part.Length>=3 && part is not ("the" or "game" or "edition"))
            .Distinct().ToArray();
        string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots=new[]{System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"RenPy"),System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Saved Games"),System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"My Games"),System.IO.Path.GetFullPath(System.IO.Path.Combine(local,"..","LocalLow")),local};
        foreach(string root in roots)
            foreach(string parent in SafeDirectories(root).Take(400))
            {
                cancellation.ThrowIfCancellationRequested();
                foreach(string candidate in new[]{parent}.Concat(SafeDirectories(parent).Take(60)))
                {
                    string candidateKey=Key(System.IO.Path.GetFileName(candidate));
                    bool nameMatch=wanted.Length>=3&&candidateKey.Contains(wanted);
                    bool partMatch=nameParts.Any(part=>candidateKey.Contains(part));
                    if(!nameMatch&&!partMatch)continue;
                    string save=System.IO.Path.Combine(candidate,"Saved","SaveGames");
                    Add(Directory.Exists(save)?save:candidate,"Passender Name unter "+System.IO.Path.GetFileName(root)+" · bitte prüfen");
                }
            }
        return found.Values.Take(30).ToList();
    }
    private static string[] SafeDirectories(string path){try{return Directory.GetDirectories(path).Where(p=>(File.GetAttributes(p)&FileAttributes.ReparsePoint)==0).ToArray();}catch{return [];}}
    private static string[] SafeFiles(string path){try{return Directory.GetFiles(path);}catch{return [];}}
    public static string Expand(string value)=>Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
}
