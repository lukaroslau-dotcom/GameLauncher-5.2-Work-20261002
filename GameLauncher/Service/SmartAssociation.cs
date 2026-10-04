using System.IO;
using System.Text.RegularExpressions;
using GameLauncher.models;
namespace GameLauncher.Service;
public sealed record Association(Game Game,int Score,string Reason,bool CanAutoLink);
public static class SmartAssociation
{
    public static string Key(string value)
    {
        value=Regex.Replace(value,@"[\[(]\s*(?:v(?:ersion)?\s*\d+(?:[._-]\d+)*|win(?:dows)?(?:32|64)?|pc|x64|x86)\s*[\])]", " ", RegexOptions.IgnoreCase);
        value=Regex.Replace(value,@"(?i)(?:^|[\s._-])(?:v(?:ersion)?\s*\d+(?:[._-]\d+)*|win(?:dows)?(?:32|64)?|pc|x64|x86|renpy|unity)(?=$|[\s._-])"," ");
        return Regex.Replace(value.ToLowerInvariant(),@"[^\p{L}\p{N}]","");
    }
    public static IReadOnlyList<Association> Rank(IEnumerable<Game> games,ScannedExecutable candidate)
    {
        string title=Key(candidate.Name),folder=Key(Path.GetFileName(candidate.GamePath.TrimEnd(Path.DirectorySeparatorChar)));
        return games.Where(g=>g.Status!=Constants.GameStatus.Deleted).Select(game=>
        {
            var keys=new[]{Key(game.Name)}.Where(k=>k.Length>=4).Concat(game.Aliases.Select(Key).Where(k=>k.Length>=3)).Distinct().ToList();
            bool exact=keys.Any(k=>k==title||k==folder);
            bool similar=!exact&&keys.Any(k=>title.Length>=5&&(title.Contains(k)||k.Contains(title)));
            bool recent=game.SearchIntentUtc.HasValue&&game.SearchIntentUtc.Value<=DateTime.UtcNow&&DateTime.UtcNow-game.SearchIntentUtc.Value<TimeSpan.FromDays(90);
                        var guesses=new[]{candidate.Name,Path.GetFileName(candidate.GamePath.TrimEnd(Path.DirectorySeparatorChar))};
            bool acronym=!exact&&new[]{game.Name}.Concat(game.Aliases).Any(name=>guesses.Any(guess=>Acronym(name,guess)));
            bool typo=!exact&&!acronym&&keys.Any(k=>new[]{title,folder}.Any(t=>k.Length>=6&&t.Length>=6&&Math.Abs(k.Length-t.Length)<=2&&Digits(k)==Digits(t)&&Distance(k,t)<= (Math.Max(k.Length,t.Length)>=12?2:1)));
            int score=exact?100:acronym?82:typo?74:similar?55:0;
            bool safe=exact&&recent&&!game.IsInstalled&&game.Status==Constants.GameStatus.Wishlist&&!candidate.IsArchive&&candidate.Confidence>=90;
            return new Association(game,score,exact?"Titel oder ausdrücklich gespeicherter Alias stimmt exakt überein":acronym?"Abkürzung passt zu den Anfangsbuchstaben; bitte bestätigen":typo?"Fast gleicher Titel mit möglichem Tippfehler; bitte bestätigen":similar?"Ähnlicher Name; Untertitel oder Fortsetzung bitte prüfen":"Kein Treffer",safe);
        }).Where(a=>a.Score>0).OrderByDescending(a=>a.Score).ToList();
    }
    private static string Digits(string value)=>string.Concat(value.Where(char.IsDigit));
    private static bool Acronym(string full,string shortName)
    {
        string shortened=Key(shortName);
        var words=Regex.Matches(Regex.Replace(full,@"(?i)\bv\d+(?:\.\d+)*\b",""),@"[\p{L}\p{N}]+").Select(m=>m.Value).Where(w=>!new[]{"windows","win","pc","x64","x86","renpy","unity"}.Contains(w.ToLowerInvariant())).ToArray();
        string initials=string.Concat(words.Select(w=>char.IsDigit(w[0])?w:w[..1])).ToLowerInvariant();
        return initials.Length>=3&&words.Length>=3&&shortened==initials&&Digits(Key(full))==Digits(shortened);
    }
    private static int Distance(string a,string b)
    {
        if(a.Length>250||b.Length>250)return 999;
        int[] previous=Enumerable.Range(0,b.Length+1).ToArray();
        for(int i=1;i<=a.Length;i++){int[] next=new int[b.Length+1];next[0]=i;for(int j=1;j<=b.Length;j++)next[j]=Math.Min(Math.Min(next[j-1]+1,previous[j]+1),previous[j-1]+(a[i-1]==b[j-1]?0:1));previous=next;}
        return previous[b.Length];
    }
    public static bool Link(GameService service,Association match,ScannedExecutable candidate)
    {
        if(candidate.IsArchive||!File.Exists(candidate.ExecutablePath)||!Directory.Exists(candidate.GamePath)||!PathSafety.Within(candidate.ExecutablePath,candidate.GamePath))return false;
        PathSafety.RejectLinks(candidate.ExecutablePath);
        if(match.Game.IsInstalled || service.FindExecutable(candidate.ExecutablePath) is Game other && other.Id!=match.Game.Id)return false;
        var game=match.Game;var before=(game.GamePath,game.ExecutablePath,game.SavePath,game.GameVersion,game.Status,game.IsInstalled,game.InstallationHistory.ToList(),game.Aliases.ToList());
        service.ApplyReinstall(game,candidate.GamePath,candidate.ExecutablePath,candidate.SavePath);
        if(!string.IsNullOrWhiteSpace(candidate.Version)&&candidate.Version!="Unknown")game.GameVersion=candidate.Version;
        if(!match.CanAutoLink && candidate.Name.Length is >=3 and <=160 && !game.Aliases.Contains(candidate.Name,StringComparer.OrdinalIgnoreCase))game.Aliases.Add(candidate.Name);
        try{service.SaveGames();return true;}catch{(game.GamePath,game.ExecutablePath,game.SavePath,game.GameVersion,game.Status,game.IsInstalled,game.InstallationHistory,game.Aliases)=before;throw;}
    }
}
