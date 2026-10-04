using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using GameLauncher.models;
using GameLauncher.Service;
internal static class CatalogChecks
{
    private sealed class Provider : IGameCatalogProvider
    {
        public string Id=>"fixture";public bool Fail;
        public Task<IReadOnlyList<CatalogResult>> SearchAsync(string query,CancellationToken cancellation)
        {cancellation.ThrowIfCancellationRequested();if(Fail)throw new IOException("offline fixture");return Task.FromResult<IReadOnlyList<CatalogResult>>(new[]{new CatalogResult("42","Test Worlds","Studio","https://f95zone.to/threads/42/")});}
    }
    public static async Task Run(string root,Action<bool,string> check)
    {
        var rows=F95CatalogProvider.Parse(Encoding.UTF8.GetBytes("""{"msg":{"data":[{"thread_id":42,"title":"Test &amp; Worlds","creator":"Studio"},{"thread_id":"../bad","title":"bad"}]}}"""));
        check(rows.Count==1&&rows[0].Title=="Test & Worlds"&&rows[0].Source.EndsWith("/42/"),"Catalog parser decodes titles and rejects malformed identities");
        var provider=new Provider();var search=new CatalogSearchService(root,provider);
        var online=await search.SearchAsync("Test",CancellationToken.None);provider.Fail=true;var offline=await search.SearchAsync("Test",CancellationToken.None);
        check(!online.FromCache&&offline.FromCache&&offline.Results.Single().Id=="42","Search cache survives provider failure with explicit offline provenance");
        var service=new GameService(new AppPaths(Path.Combine(root,"Association")));
        var game=CatalogSearchService.Remember(service,new("42","Test Worlds","Studio","https://f95zone.to/threads/42/"),"Test");service.SaveGames();
        CatalogSearchService.Remember(service,new("42","Test Worlds","Studio","https://f95zone.to/threads/42/"),"Test");
        check(service.GetGames().Count==1&&game.Aliases.Count==0&&game.SearchIntentUtc!=null,"Remembering is idempotent and broad search queries never become matching aliases");
        string folder=Path.Combine(root,"Test Worlds v1.2 Windows");Directory.CreateDirectory(folder);string exe=Path.Combine(folder,"Game.exe");File.WriteAllText(exe,"fixture");
        var candidate=new ScannedExecutable {Name="Test Worlds v1.2 Windows",GamePath=folder,ExecutablePath=exe,Confidence=95,Version="1.2"};
        var match=SmartAssociation.Rank(service.GetGames(),candidate).Single();
        check(match.CanAutoLink&&match.Score==100,"Recent explicitly remembered exact title is eligible for harmless association");
        check(SmartAssociation.Key("Test Worlds 2")!=SmartAssociation.Key("Test Worlds")&&SmartAssociation.Key("Test Worlds [Part 2]")!=SmartAssociation.Key("Test Worlds"),"Association does not erase sequel numbers or subtitle identities");
        var uncertain=new ScannedExecutable {Name="Test Worlds 2",GamePath=Path.Combine(root,"Test Worlds 2"),Confidence=95};
        check(!SmartAssociation.Rank(service.GetGames(),uncertain).Single().CanAutoLink,"Similar sequel titles require confirmation");
        var acronymGame=new Game {Id="acronym",Name="The Silver Kingdom",Status=GameLauncher.Constants.GameStatus.Wishlist,IsInstalled=false,SearchIntentUtc=DateTime.UtcNow};
        var abbr=new ScannedExecutable {Name="TSK v1.2 Windows",GamePath=Path.Combine(root,"TSK"),Confidence=95};
        var abbrMatch=SmartAssociation.Rank(new[]{acronymGame},abbr).Single();
        check(abbrMatch.Score==82&&!abbrMatch.CanAutoLink,"Abbreviation matches initial letters but always asks for confirmation");
        var typoMatch=SmartAssociation.Rank(new[]{game},new ScannedExecutable{Name="Test Worlss",GamePath=Path.Combine(root,"Test Worlss"),Confidence=95}).Single();
        check(typoMatch.Score==74&&!typoMatch.CanAutoLink,"One-character typo produces an explained nonautomatic suggestion");
        check(SmartAssociation.Rank(new[]{acronymGame},new ScannedExecutable{Name="TSK 2",GamePath="TSK 2",Confidence=95}).Count==0,"Abbreviation does not hide a sequel number");
        check(SmartAssociation.Rank(new[]{game,new Game {Id="same",Name=game.Name}},candidate).Count==2,"Ambiguous exact titles remain separate suggestions");
        var namedWish=new Game {Id="name-only",Name="Name Only",Status=GameLauncher.Constants.GameStatus.Wishlist,IsInstalled=false,Notes="keep"};service.AddGame(namedWish);
        var enriched=CatalogSearchService.Remember(service,new("77","Name Only","Studio","https://f95zone.to/threads/77/"),"Name Only");
        check(ReferenceEquals(namedWish,enriched)&&enriched.Notes=="keep"&&enriched.F95ThreadId=="77","Catalog selection enriches unique name-only wish without duplicating its identity");
        var acronymService=new GameService(new AppPaths(Path.Combine(root,"AcronymLibrary")));acronymService.AddGame(acronymGame);
        Directory.CreateDirectory(abbr.GamePath);abbr.ExecutablePath=Path.Combine(abbr.GamePath,"game.exe");File.WriteAllText(abbr.ExecutablePath,"fixture, never run");
        check(SmartAssociation.Link(acronymService,abbrMatch,abbr)&&acronymGame.Aliases.Contains(abbr.Name),"Confirmed abbreviation is learned as an alias without creating a duplicate");
        check(new GameService(acronymService.Paths).ShowGame(acronymGame.Id)!.Aliases.Contains(abbr.Name),"Learned alias survives restart");
        check(SmartAssociation.Rank(new[]{acronymGame},abbr).Single().Score==100,"Explicitly learned three-letter alias becomes an exact match without switching installed games");
        var images=F95CatalogProvider.Parse(Encoding.UTF8.GetBytes("""{"msg":{"data":[{"thread_id":43,"title":"Cover fixture","cover":"https://preview.f95zone.to/cover.png","version":"1.2"}]}}"""));
        check(images.Single().CoverUrl.EndsWith("cover.png")&&images.Single().Version=="1.2","Catalog exposes lightweight cover and version without fetching full metadata for each row");
        game.Tags.Add("adventure");game.Notes="manual";string originalId=game.Id;
        check(SmartAssociation.Link(service,match,candidate)&&game.Id==originalId&&game.Tags.Contains("adventure")&&game.Notes=="manual"&&game.IsInstalled,"Association preserves ID and metadata and only attaches validated local paths");
        check(!SmartAssociation.Rank(service.GetGames(),candidate).Single().CanAutoLink,"An installed game is never automatically switched to another installation");
        using var json=JsonDocument.Parse("""{"tags":"[5, 999999]","unknown_tags":"[\"custom tag\"]"}""");
        var tags=MetadataEnrichment.ReadTags(json.RootElement);
        check(tags.Contains("adventure")&&tags.Contains("custom tag")&&!tags.Contains("999999"),"Metadata resolves known tag IDs, preserves custom tags and does not invent unknown labels");
        game.UserEditedFields.Add(nameof(Game.F95Developer));game.UserEditedFields.Add(nameof(Game.Tags));game.F95Developer="Manual studio";
        MetadataEnrichment.Apply(game,new("42","Test","2","Remote studio","Active","",Array.Empty<string>()){Tags=new[]{"remote"},Description="Description"});
        check(game.F95Developer=="Manual studio"&&!game.Tags.Contains("remote")&&game.Description=="Description","Metadata enrichment preserves manually edited developer and tags");
        service.SaveGames();var reread=new GameService(service.Paths).ShowGame(game.Id)!;
        check(reread.SearchIntentUtc!=null&&reread.F95Developer=="Manual studio"&&reread.Description=="Description","New metadata and remembered search intent survive restart");
    }
}
