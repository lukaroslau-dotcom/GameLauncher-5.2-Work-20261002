using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
namespace GameLauncher.Service;
public sealed record DownloadProgress(long Bytes,long? Total,double BytesPerSecond);
public sealed record DownloadState(string Url,string? ETag,long? Total,string? ExpectedSha256=null);
/// <summary>Only writes into app-owned job directories. Caller uses a no-redirect HttpClient.</summary>
public sealed class DownloadService(HttpClient client,string root,long maximumBytes=20L*1024*1024*1024)
{
    public async Task<string> Download(Guid job,Uri url,IProgress<DownloadProgress>? progress=null,CancellationToken cancellationToken=default,string? expectedSha256=null)
    {
        for(int attempt=0;;attempt++)
        {
            try{return await Attempt(job,url,progress,cancellationToken,expectedSha256);}
            catch(Exception e)when(attempt<2&&(e is HttpRequestException or TemporaryDownloadException))
            {
                var delay=e is TemporaryDownloadException temporary?temporary.Delay:TimeSpan.FromSeconds(attempt+1);
                await Task.Delay(delay,cancellationToken);
            }
        }
    }
    private static string HashFile(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    sealed class TemporaryDownloadException(string message,TimeSpan delay):IOException(message){public TimeSpan Delay{get;}=delay;}
    async Task<string> Attempt(Guid job,Uri url,IProgress<DownloadProgress>? progress,CancellationToken cancellationToken,string? expectedSha256)
    {
        if(job==Guid.Empty||url.Scheme!="https"||!string.IsNullOrEmpty(url.UserInfo)||url.IsLoopback) throw new IOException("Nur explizite öffentliche HTTPS-Downloads sind erlaubt.");
        if(expectedSha256!=null&&(expectedSha256.Length!=64||expectedSha256.Any(c=>!Uri.IsHexDigit(c))))throw new IOException("Ungültiger erwarteter SHA-256.");
        var directory=Path.Combine(Path.GetFullPath(root),job.ToString()); PathSafety.RejectLinks(directory); Directory.CreateDirectory(directory);
        var partial=Path.Combine(directory,"download.partial"); var sidecar=Path.Combine(directory,"state.json");var final=Path.Combine(directory,"download.complete");
        if(File.Exists(final)) throw new IOException("Download bereits abgeschlossen; Datei wird nicht überschrieben.");
        PathSafety.RejectLinks(partial);PathSafety.RejectLinks(sidecar);
        if(File.Exists(sidecar)&&new FileInfo(sidecar).Length>65536)throw new IOException("Downloadzustand zu groß.");
        var state=File.Exists(sidecar)?JsonSerializer.Deserialize<DownloadState>(await File.ReadAllTextAsync(sidecar,cancellationToken)):null;
        if(state!=null&&(state.Url!=url.AbsoluteUri||!string.Equals(state.ExpectedSha256,expectedSha256,StringComparison.OrdinalIgnoreCase))) throw new IOException("Downloadquelle passt nicht zum bestehenden Auftrag.");
        long offset=File.Exists(partial)?new FileInfo(partial).Length:0;
        if(offset>maximumBytes) throw new IOException("Downloadlimit überschritten.");
        if(offset>0&&(state?.ETag==null||state.ETag.StartsWith("W/"))) offset=0;
        using var request=new HttpRequestMessage(HttpMethod.Get,url);
        request.Headers.AcceptEncoding.ParseAdd("identity");
        if(offset>0) { request.Headers.Range=new RangeHeaderValue(offset,null);request.Headers.IfRange=new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(state!.ETag!)); }
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
        if(response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout)
        {
            var delay=response.Headers.RetryAfter?.Delta??TimeSpan.FromSeconds(1);
            throw new TemporaryDownloadException("Downloadquelle vorübergehend nicht erreichbar: "+(int)response.StatusCode,TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds,0,30)));
        }
        if(response.StatusCode is not HttpStatusCode.OK and not HttpStatusCode.PartialContent) throw new IOException("Downloadserver meldet "+(int)response.StatusCode+"; keine automatische Weiterleitung.");
        if(response.RequestMessage?.RequestUri!=null&&response.RequestMessage.RequestUri!=url) throw new IOException("Automatische Weiterleitung abgelehnt.");
        if(response.Content.Headers.ContentEncoding.Count>0) throw new IOException("Unerwartete Inhaltskodierung.");
        long? total;
        if(response.StatusCode==HttpStatusCode.PartialContent)
        {
            var range=response.Content.Headers.ContentRange;
            if(offset==0||range?.Unit!="bytes"||range.From!=offset||range.To==null||range.Length==null||range.To!=range.Length-1||response.Headers.ETag?.ToString()!=state!.ETag||state.Total!=range.Length) throw new IOException("Range/ETag stimmen nicht; vorhandene Bytes bleiben unverändert.");
            total=range.Length;
        }
        else { offset=0;total=response.Content.Headers.ContentLength; }
        if(total>maximumBytes) throw new IOException("Download überschreitet Größenlimit.");
        var next=new DownloadState(url.AbsoluteUri,response.Headers.ETag?.ToString(),total,expectedSha256);
        // Invalidate/truncate old bytes BEFORE publishing new resume validators.
        using(var reset=new FileStream(partial,FileMode.OpenOrCreate,FileAccess.Write,FileShare.None)) { reset.SetLength(offset);reset.Flush(true); }
        var tmp=sidecar+".new";PathSafety.RejectLinks(tmp);await File.WriteAllTextAsync(tmp,JsonSerializer.Serialize(next),cancellationToken); File.Move(tmp,sidecar,true);
        using var input=await response.Content.ReadAsStreamAsync(cancellationToken);
        using(var output=new FileStream(partial,FileMode.Open,FileAccess.Write,FileShare.None,81920,true))
        {
            output.Position=offset; var timer=System.Diagnostics.Stopwatch.StartNew();long received=0;long lastReport=0;var buffer=new byte[81920];int count;
            while((count=await input.ReadAsync(buffer,cancellationToken))>0)
            {
                if(output.Position+count>maximumBytes||total!=null&&output.Position+count>total) throw new IOException("Server überschreitet angekündigte Größe.");
                await output.WriteAsync(buffer.AsMemory(0,count),cancellationToken);received+=count;
                if(timer.ElapsedMilliseconds-lastReport>=250) { progress?.Report(new(output.Position,total,received/Math.Max(timer.Elapsed.TotalSeconds,.001)));lastReport=timer.ElapsedMilliseconds; }
            }
            await output.FlushAsync(cancellationToken);output.Flush(true);
            if(total!=null&&output.Length!=total) throw new IOException("Download unvollständig; Fortsetzen möglich.");
            progress?.Report(new(output.Position,total,0));
        }
        if(expectedSha256!=null&&!string.Equals(HashFile(partial),expectedSha256,StringComparison.OrdinalIgnoreCase))throw new IOException("SHA-256 stimmt nicht mit dem Herausgeber-Feed überein. Datei bleibt unvollständig markiert und wird nicht installiert.");
        cancellationToken.ThrowIfCancellationRequested();File.Move(partial,final,false);return final;
    }
}
