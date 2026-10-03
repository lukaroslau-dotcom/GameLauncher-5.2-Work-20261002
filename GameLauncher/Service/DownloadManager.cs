using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace GameLauncher.Service;

public sealed class DownloadJob
{
    public Guid Id { get; set; }
    public string Url { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? Sha256 { get; set; }
    public string Status { get; set; } = "Bereit";
    public string Error { get; set; } = "";
    public long Bytes { get; set; }
    public long? Total { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public double BytesPerSecond { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public double Percent => Total > 0 ? 100d * Bytes / Total.Value : 0;
    [System.Text.Json.Serialization.JsonIgnore] public string Detail => $"{Status} · {GameSizeService.Format(Bytes)} / {GameSizeService.Format(Total)} · {GameSizeService.Format((long)BytesPerSecond)}/s {Error}";
}

public sealed class DownloadManager : IDisposable
{
    private readonly string root;
    private readonly HttpClient client;
    private readonly DownloadService transfer;
    private readonly Dictionary<Guid, CancellationTokenSource> active = new();
    public List<DownloadJob> Jobs { get; } = new();
    public bool IsActive => active.Count > 0;
    public DownloadManager(string directory)
    {
        root = Path.GetFullPath(directory);
        PathSafety.RejectLinks(root); Directory.CreateDirectory(root);
        client = PublicDownloadClient.Create();
        transfer = new DownloadService(client, root);
        foreach (string folder in Directory.EnumerateDirectories(root).Take(1000))
        {
            if (!Guid.TryParseExact(Path.GetFileName(folder), "D", out Guid id)) continue;
            try
            {
                string metadata = Path.Combine(folder, "job.json"); PathSafety.RejectLinks(metadata);
                if (!File.Exists(metadata) || new FileInfo(metadata).Length > 65536) continue;
                var job = JsonSerializer.Deserialize<DownloadJob>(File.ReadAllText(metadata));
                if (job == null || job.Id != id || !ValidFileName(job.FileName)) continue;
                if (File.Exists(FinalPath(job))) job.Status = "Abgeschlossen";
                else if (job.Status != "Abgebrochen") job.Status = "Pausiert";
                Jobs.Add(job);
            }
            catch (Exception) { /* Corrupt job is preserved for diagnosis, never executed. */ }
        }
    }
    public DownloadJob Add(string url, string fileName, string? hash)
    {
        if (Jobs.Count >= 1000) throw new IOException("Maximal 1000 gespeicherte Downloads.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.IsLoopback || uri.UserInfo.Length != 0 || uri.Port != 443)
            throw new IOException("Bitte eine öffentliche HTTPS-Datei-URL ohne Zugangsdaten angeben.");
        if (!ValidFileName(fileName)) throw new IOException("Bitte einen Dateinamen ohne Ordner oder Sonderpfade angeben.");
        hash = string.IsNullOrWhiteSpace(hash) ? null : hash.Trim();
        if (hash != null && (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c)))) throw new IOException("SHA-256 muss 64 Hex-Zeichen enthalten.");
        var job = new DownloadJob { Id = Guid.NewGuid(), Url = uri.AbsoluteUri, FileName = fileName, Sha256 = hash };
        Save(job); Jobs.Add(job); return job;
    }
    public string FinalPath(DownloadJob job) => Path.Combine(root, job.Id.ToString(), job.FileName);
    public async Task RunAsync(DownloadJob job)
    {
        if (!Jobs.Contains(job) || active.ContainsKey(job.Id) || job.Status == "Abgeschlossen") return;
        if (active.Count >= 3) throw new IOException("Maximal drei Downloads gleichzeitig.");
        var cancellation = new CancellationTokenSource(TimeSpan.FromHours(4)); active.Add(job.Id, cancellation);
        try
        {
            job.Status = "Läuft"; job.Error = ""; Save(job);
            var progress = new Progress<DownloadProgress>(p => { if (job.Status == "Läuft") { job.Bytes = p.Bytes; job.Total = p.Total; job.BytesPerSecond = p.BytesPerSecond; } });
            string complete = Path.Combine(root, job.Id.ToString(), "download.complete");
            if (!File.Exists(complete)) complete = await transfer.Download(job.Id, new Uri(job.Url), progress, cancellation.Token, job.Sha256);
            PathSafety.RejectLinks(complete); PathSafety.RejectLinks(FinalPath(job));
            if (job.Sha256 != null)
            {
                using var input = File.OpenRead(complete);
                if (!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)).Equals(job.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Abgeschlossene Zwischendatei stimmt nicht mit SHA-256 überein.");
            }
            cancellation.Token.ThrowIfCancellationRequested();
            File.Move(complete, FinalPath(job), false);
            job.Bytes = new FileInfo(FinalPath(job)).Length; job.Total = job.Bytes; job.Status = "Abgeschlossen";
        }
        catch (OperationCanceledException) { if (job.Status != "Abgebrochen") job.Status = "Pausiert"; }
        catch (Exception ex) { job.Status = "Fehler"; job.Error = ex.Message; }
        finally { active.Remove(job.Id); cancellation.Dispose(); job.BytesPerSecond = 0; Save(job); }
    }
    public void Pause(DownloadJob job, bool cancel = false)
    {
        if (job.Status == "Abgeschlossen") return;
        job.Status = cancel ? "Abgebrochen" : "Pausiert";
        if (active.TryGetValue(job.Id, out var cancellation)) cancellation.Cancel();
        Save(job); // Partial bytes retained; restart explicitly reuses or resets validators.
    }
    public void PauseAll() { foreach (var job in Jobs.Where(j => active.ContainsKey(j.Id))) Pause(job); }
    private void Save(DownloadJob job)
    {
        string folder = Path.Combine(root, job.Id.ToString()); PathSafety.RejectLinks(folder); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "job.json"); string temp = path + ".new";
        PathSafety.RejectLinks(path); PathSafety.RejectLinks(temp);
        File.WriteAllText(temp, JsonSerializer.Serialize(job)); File.Move(temp, path, true);
    }
    private static bool ValidFileName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 180 &&
        name == Path.GetFileName(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.EndsWith('.') && !name.EndsWith(' ') &&
        !new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "JOB", "STATE", "DOWNLOAD" }.Contains(name.Split('.')[0].ToUpperInvariant());
    public void Dispose() { PauseAll(); client.Dispose(); }
}

public static class PublicDownloadClient
{
    private static readonly System.Net.IPNetwork[] Denied = new[] { "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12", "192.0.0.0/24", "192.0.2.0/24", "192.168.0.0/16", "198.18.0.0/15", "198.51.100.0/24", "203.0.113.0/24", "224.0.0.0/4", "240.0.0.0/4", "2001:db8::/32", "2002::/16", "2001::/32" }.Select(System.Net.IPNetwork.Parse).ToArray();
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && !System.Net.IPNetwork.Parse("2000::/3").Contains(address)) return false;
        return !Denied.Any(n => n.BaseAddress.AddressFamily == address.AddressFamily && n.Contains(address));
    }
    public static HttpClient Create()
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(20) };
        handler.ConnectCallback = async (context, token) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
            if (context.DnsEndPoint.Port != 443 || addresses.Length == 0 || addresses.Any(a => !IsPublic(a))) throw new IOException("Lokale oder reservierte Downloadadresse abgelehnt.");
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(addresses, 443, token); return new NetworkStream(socket, true); }
            catch { socket.Dispose(); throw; }
        };
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }
}
