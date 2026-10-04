using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameLauncher.models;
namespace GameLauncher.Service;

public sealed record CatalogResult(string Id, string Title, string Creator, string Source)
{ public string CoverUrl {get;init;} = ""; public string Version {get;init;} = ""; }
public sealed record CatalogResponse(IReadOnlyList<CatalogResult> Results, bool FromCache, string Notice);
/// <summary>Extension boundary: an authorized account adapter can implement this without changing library or downloads.</summary>
public interface IGameCatalogProvider
{
    string Id { get; }
    Task<IReadOnlyList<CatalogResult>> SearchAsync(string query, CancellationToken cancellation);
}
public sealed class F95CatalogProvider : IGameCatalogProvider, IDisposable
{
    public string Id => "f95-public";
    private readonly HttpClient client;
    private readonly bool ownsClient;
    public F95CatalogProvider(HttpClient? supplied = null) { ownsClient=supplied==null;client=supplied??PublicDownloadClient.Create(); }
    public async Task<IReadOnlyList<CatalogResult>> SearchAsync(string query,CancellationToken cancellation)
    {
        string url="https://f95zone.to/sam/latest_alpha/latest_data.php?cmd=list&cat=games&page=1&search="+Uri.EscapeDataString(query)+"&sort=likes&rows=25";
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request=new HttpRequestMessage(HttpMethod.Get,url);request.Headers.UserAgent.ParseAdd("GameLauncher/5.2");request.Headers.Accept.ParseAdd("application/json");
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized || (int)response.StatusCode is >=300 and <400)
            throw new IOException("Die Quelle verlangt Anmeldung oder eine Browserprüfung. Keine automatische Umgehung; vorhandene lokale Treffer bleiben nutzbar.");
        response.EnsureSuccessStatusCode();
        using var input=await response.Content.ReadAsStreamAsync(timeout.Token);using var output=new MemoryStream();byte[] buffer=new byte[16384];int read;
        while((read=await input.ReadAsync(buffer,timeout.Token))>0){if(output.Length+read>2*1024*1024)throw new IOException("Suchantwort zu groß.");output.Write(buffer,0,read);}
        return Parse(output.ToArray());
    }
    public static IReadOnlyList<CatalogResult> Parse(byte[] bytes)
    {
        using var json=JsonDocument.Parse(bytes);
        if(!json.RootElement.TryGetProperty("msg",out var msg)||msg.ValueKind!=JsonValueKind.Object||!msg.TryGetProperty("data",out var rows)||rows.ValueKind!=JsonValueKind.Array)
            throw new IOException("Die Quelle hat keine lesbare Spieleliste geliefert.");
        var result=new List<CatalogResult>();
        foreach(var row in rows.EnumerateArray().Take(50))
        {
            string Field(string key)=>row.TryGetProperty(key,out var value)?WebUtility.HtmlDecode(value.ToString()).Trim():"";
            string id=Field("thread_id"),title=Field("title"),creator=Field("creator");
            if(id.Length is <1 or >20 || !id.All(char.IsAsciiDigit) || id.All(c=>c=='0') || title.Length is <1 or >500)continue;
            string cover=Field("cover"),version=Field("version");
            if(cover.Length>2048||!Uri.TryCreate(cover,UriKind.Absolute,out var coverUri)||coverUri.Scheme!="https")cover="";
            result.Add(new(id,title,creator.Length>300?creator[..300]:creator,"https://f95zone.to/threads/"+id+"/"){CoverUrl=cover,Version=version.Length<=100?version:""});
        }
        return result.DistinctBy(r=>r.Id).ToList();
    }
    public void Dispose(){if(ownsClient)client.Dispose();}
}
public sealed class CatalogSearchService(string root, IGameCatalogProvider provider)
{
    public async Task<CatalogResponse> SearchAsync(string query, CancellationToken cancellation)
    {
        query=query.Trim();if(query.Length is <2 or >160)throw new IOException("Bitte 2 bis 160 Zeichen eingeben.");
        string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(provider.Id+"|"+query.ToLowerInvariant())));
        string file=Path.Combine(root,"SearchCache",key+".json");PathSafety.RejectLinks(file);
        try
        {
            var results=await provider.SearchAsync(query,cancellation);
            try {AtomicJsonFile.Write(file,results);}catch(IOException){return new(results,false,"Treffer geladen; Offlinecache konnte nicht gespeichert werden.");}
            return new(results,false,results.Count+" Treffer · Quelle: "+provider.Id);
        }
        catch(Exception ex) when(ex is not OperationCanceledException || !cancellation.IsCancellationRequested)
        {
            if(File.Exists(file)&&new FileInfo(file).Length<2*1024*1024)
            {
                var cached=JsonSerializer.Deserialize<List<CatalogResult>>(File.ReadAllText(file))??new();
                return new(cached,true,"Offlinecache vom "+File.GetLastWriteTime(file).ToString("g")+" · "+ex.Message);
            }
            throw new IOException("Suche nicht verfügbar: "+ex.Message,ex);
        }
    }
    public static Game Remember(GameService service,CatalogResult result,string query)
    {
        var existing=service.GetGames().FirstOrDefault(g=>g.F95ThreadId==result.Id || g.InstallSource==result.Source);
        if(existing==null)
        {
            var wishes=service.GetGames().Where(g=>!g.IsInstalled&&g.Status==Constants.GameStatus.Wishlist&&string.IsNullOrWhiteSpace(g.F95ThreadId)&&SmartAssociation.Key(g.Name)==SmartAssociation.Key(result.Title)).ToList();
            if(wishes.Count==1){existing=wishes[0];existing.F95ThreadId=result.Id;existing.InstallSource=result.Source;}
        }
        if(existing!=null){existing.SearchIntentUtc=DateTime.UtcNow;if(SmartAssociation.Key(query)==SmartAssociation.Key(result.Title)&&!existing.Aliases.Contains(query,StringComparer.OrdinalIgnoreCase))existing.Aliases.Add(query);return existing;}
        var game=new Game {Id=Guid.NewGuid().ToString(),Name=result.Title,F95ThreadId=result.Id,F95Developer=result.Creator,InstallSource=result.Source,Status=Constants.GameStatus.Wishlist,IsInstalled=false,AddedDate=DateTime.Now,SearchIntentUtc=DateTime.UtcNow};
        // Only an exact title query can become a matching alias; broad search terms are never aliases.
        if(SmartAssociation.Key(query)==SmartAssociation.Key(result.Title))game.Aliases.Add(query);
        service.AddGame(game);return game;
    }
}
