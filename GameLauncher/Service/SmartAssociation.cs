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
            var keys=new[]{game.Name}.Concat(game.Aliases).Select(Key).Where(k=>k.Length>=4).ToList();
            bool exact=keys.Any(k=>k==title||k==folder);
            bool similar=!exact&&keys.Any(k=>title.Length>=5&&(title.Contains(k)||k.Contains(title)));
            bool recent=game.SearchIntentUtc.HasValue&&DateTime.UtcNow-game.SearchIntentUtc.Value<TimeSpan.FromDays(90);
            int score=exact?100:similar?55:0;
            bool safe=exact&&recent&&!game.IsInstalled&&game.Status==Constants.GameStatus.Wishlist&&!candidate.IsArchive&&candidate.Confidence>=90;
            return new Association(game,score,exact?"Titel oder ausdrücklich gespeicherter Alias stimmt exakt überein":similar?"Ähnlicher Name; bitte prüfen":"Kein Treffer",safe);
        }).Where(a=>a.Score>0).OrderByDescending(a=>a.Score).ToList();
    }
    public static bool Link(GameService service,Association match,ScannedExecutable candidate)
    {
        if(candidate.IsArchive||!File.Exists(candidate.ExecutablePath)||!Directory.Exists(candidate.GamePath)||!PathSafety.Within(candidate.ExecutablePath,candidate.GamePath))return false;
        PathSafety.RejectLinks(candidate.ExecutablePath);
        if(match.Game.IsInstalled || service.FindExecutable(candidate.ExecutablePath) is Game other && other.Id!=match.Game.Id)return false;
        var game=match.Game;var before=(game.GamePath,game.ExecutablePath,game.SavePath,game.GameVersion,game.Status,game.IsInstalled,game.InstallationHistory.ToList());
        service.ApplyReinstall(game,candidate.GamePath,candidate.ExecutablePath,candidate.SavePath);
        if(!string.IsNullOrWhiteSpace(candidate.Version)&&candidate.Version!="Unknown")game.GameVersion=candidate.Version;
        try{service.SaveGames();return true;}catch{(game.GamePath,game.ExecutablePath,game.SavePath,game.GameVersion,game.Status,game.IsInstalled,game.InstallationHistory)=before;throw;}
    }
}
