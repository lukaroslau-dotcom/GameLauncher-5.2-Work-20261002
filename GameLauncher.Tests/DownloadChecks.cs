using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameLauncher.Service;

internal static class DownloadChecks
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request)); }
    public static async Task Run(string root, Action<bool, string> check)
    {
        string directory = Path.Combine(root, "Downloads");
        var url = new Uri("https://example.com/game.zip");
        byte[] payload = Encoding.UTF8.GetBytes("complete payload");
        string hash = Convert.ToHexString(SHA256.HashData(payload));
        HttpResponseMessage Full(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        using var client = new HttpClient(new Handler(_ => Full(payload)));
        var service = new DownloadService(client, directory);
        Guid id = Guid.NewGuid();
        string file = await service.Download(id, url, expectedSha256: hash);
        check(File.ReadAllBytes(file).SequenceEqual(payload), "HTTPS download verifies publisher SHA-256 before completion");
        bool mismatch = false; Guid badId = Guid.NewGuid();
        try { await service.Download(badId, url, expectedSha256: new string('0', 64)); } catch (IOException) { mismatch = true; }
        check(mismatch && !File.Exists(Path.Combine(directory, badId.ToString(), "download.complete")), "Hash mismatch never publishes a completed file");
        bool replaced = false;
        try { await service.Download(id, url); } catch (IOException) { replaced = true; }
        check(replaced && File.ReadAllBytes(file).SequenceEqual(payload), "Completed download is not overwritten");
        Guid resumed = Guid.NewGuid(); string folder = Path.Combine(directory, resumed.ToString()); Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "download.partial"), payload[..4]);
        File.WriteAllText(Path.Combine(folder, "state.json"), JsonSerializer.Serialize(new DownloadState(url.AbsoluteUri, "\"v1\"", payload.Length, hash)));
        using var rangeClient = new HttpClient(new Handler(request =>
        {
            check(request.Headers.Range?.Ranges.Single().From == 4 && request.Headers.IfRange?.EntityTag?.Tag == "\"v1\"", "Resume sends exact Range and If-Range validator");
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(payload[4..]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(4, payload.Length - 1, payload.Length);
            response.Headers.ETag = new EntityTagHeaderValue("\"v1\""); return response;
        }));
        string result = await new DownloadService(rangeClient, directory).Download(resumed, url, expectedSha256: hash);
        check(File.ReadAllBytes(result).SequenceEqual(payload), "Resume combines bytes without duplication and verifies final hash");
        Guid restart = Guid.NewGuid(); string restartFolder = Path.Combine(directory, restart.ToString()); Directory.CreateDirectory(restartFolder);
        File.WriteAllText(Path.Combine(restartFolder, "download.partial"), "old");
        File.WriteAllText(Path.Combine(restartFolder, "state.json"), JsonSerializer.Serialize(new DownloadState(url.AbsoluteUri, "\"old\"", 100)));
        check(File.ReadAllBytes(await service.Download(restart, url)).SequenceEqual(payload), "HTTP 200 resets stale partial bytes before new validators");
        using var redirectedClient = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)));
        bool redirect = false;
        try { await new DownloadService(redirectedClient, directory).Download(Guid.NewGuid(), url); } catch (IOException) { redirect = true; }
        check(redirect, "Download redirects require explicit source selection");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        bool cancelled = false;
        try { await service.Download(Guid.NewGuid(), url, cancellationToken: canceled.Token); } catch (OperationCanceledException) { cancelled = true; }
        check(cancelled, "Download cancellation interrupts transfer");
        check(new[] { "127.0.0.1", "192.168.1.5", "10.1.2.3", "169.254.169.254", "::1", "fe80::1", "::ffff:127.0.0.1" }.All(a => !PublicDownloadClient.IsPublic(IPAddress.Parse(a))), "Public downloader rejects private, loopback and link-local addresses");
        check(PublicDownloadClient.IsPublic(IPAddress.Parse("8.8.8.8")), "Public address policy admits public IPv4");
        string jobs = Path.Combine(root, "PersistentJobs"); Guid persisted;
        using (var manager = new DownloadManager(jobs))
        {
            var job = manager.Add(url.AbsoluteUri, "game.zip", hash); persisted = job.Id;
            manager.Pause(job, true);
        }
        using var reloaded = new DownloadManager(jobs);
        check(reloaded.Jobs.Single().Id == persisted && reloaded.Jobs.Single().Status == "Abgebrochen", "Cancelled job survives restart without starting network traffic");
    }
}
