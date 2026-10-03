using GameLauncher.models;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameLauncher.Service;

public sealed record F95UpdateReport(
    int Eligible,
    int Checked,
    int Updates,
    int MissingInstalledVersion,
    int Failed,
    IReadOnlyList<Game> NewUpdates);

public sealed record F95Metadata(
    string ThreadId,
    string Name,
    string Version,
    string Developer,
    string Status,
    string ImageUrl,
    IReadOnlyList<string> PreviewUrls)
{
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public string Description { get; init; } = "";
    public string Engine { get; init; } = "";
}

public sealed class F95UpdateService : IDisposable
{
    private const int BatchSize = 10;
    private const int MaxPayloadBytes = 8 * 1024 * 1024;
    private readonly HttpClient client;
    private readonly HttpClient imageClient;
    private readonly bool ownsClient;

    public F95UpdateService(HttpClient? httpClient = null)
    {
        ownsClient = httpClient == null;
        var handler = CreateIpv4PreferringHandler();
        client = httpClient ?? new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.f95checker.dev/"),
            Timeout = TimeSpan.FromSeconds(20),
            MaxResponseContentBufferSize = MaxPayloadBytes
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GameLauncher/3.1 (private update checker)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        imageClient = PublicDownloadClient.Create(); imageClient.Timeout = TimeSpan.FromSeconds(25);
        imageClient.DefaultRequestHeaders.UserAgent.ParseAdd("GameLauncher/3.1 (cover loader)");
    }

    private static SocketsHttpHandler CreateIpv4PreferringHandler()
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            UseProxy = false,
            ConnectCallback = async (context, cancellationToken) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
                var ipv4 = addresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);
                if (ipv4 == null)
                    throw new HttpRequestException("Keine IPv4-Adresse für " + context.DnsEndPoint.Host + " gefunden.");

                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true
                };
                await socket.ConnectAsync(ipv4, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
        };
    }

    public static bool TryExtractThreadId(string? source, out string threadId)
    {
        threadId = "";
        if (!Uri.TryCreate(source?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !IsF95Host(uri.Host))
            return false;

        string[] parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int threads = Array.FindIndex(parts, p => p.Equals("threads", StringComparison.OrdinalIgnoreCase));
        if (threads < 0 || threads + 1 >= parts.Length) return false;
        Match match = Regex.Match(Uri.UnescapeDataString(parts[threads + 1]), @"(?:^|\.)([1-9]\d*)$");
        if (!match.Success) return false;
        threadId = match.Groups[1].Value;
        return threadId.Length <= 20;
    }

    public static string? NormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string text = WebUtility.HtmlDecode(value).Normalize().Trim();
        text = Regex.Replace(text, "[\u200B-\u200D\u2060\uFEFF]", "");
        text = Regex.Replace(text, @"\s+", " ");
        if (Regex.IsMatch(text, @"^(?:-|—|–|n/?a|none|unknown|unbekannt|tbd|\?)$", RegexOptions.IgnoreCase))
            return null;

        // Prefer an explicit bracketed version when a title was pasted with it.
        Match bracket = Regex.Match(text,
            @"[\[(]\s*((?:(?:version|ver\.?)(?=\s|[:=._-])|v(?=\s|[:=._-]|\d))\s*[:=._-]?\s*(?=[^\])]*\d)[^\])]+)\s*[\])]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!bracket.Success)
            bracket = Regex.Matches(text, @"[\[(]\s*((?=[^\])]*\d)[^\])]+)\s*[\])]",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Cast<Match>().FirstOrDefault(m => !Regex.IsMatch(m.Groups[1].Value, @"^\s*\d{1,2}\s*\+\s*$")) ?? Match.Empty;
        if (bracket.Success) text = bracket.Groups[1].Value;
        for (int pass = 0; pass < 3; pass++)
        {
            string before = text;
            text = text.Trim().Trim('[', ']', '(', ')').Trim();
            text = Regex.Replace(text,
                @"^(?:(?:version|ver\.?)(?=\s|[:=._-])|v(?=\s|[:=._-]|\d))\s*[:=._-]?\s*", "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (text == before) break;
        }
        text = Regex.Replace(text, @"\s*([._-])\s*", "$1");
        text = Regex.Replace(text, @"\s+", " ");
        return text.Length == 0 ? null : text.ToLowerInvariant();
    }

    public static bool VersionsDiffer(string? installed, string? online)
    {
        string? left = NormalizeVersion(installed);
        string? right = NormalizeVersion(online);
        return left != null && right != null && !StringComparer.OrdinalIgnoreCase.Equals(left, right);
    }

    public static bool IsUpdateAvailable(string? installed, string? online, string? ignoredOnlineVersion)
    {
        string? normalizedOnline = NormalizeVersion(online);
        return VersionsDiffer(installed, online) && normalizedOnline != null &&
            !StringComparer.OrdinalIgnoreCase.Equals(normalizedOnline, NormalizeVersion(ignoredOnlineVersion));
    }

    public async Task<F95Metadata> FetchMetadataAsync(string source, CancellationToken cancellationToken)
    {
        if (!TryExtractThreadId(source, out string threadId))
            throw new ArgumentException("Der Link ist kein gültiger F95Zone-Thread.", nameof(source));
        using JsonDocument fast = await GetJsonAsync("fast?ids=" + threadId, cancellationToken);
        Dictionary<string, string> stamps = ParseFast(fast.RootElement);
        if (!stamps.TryGetValue(threadId, out string? stamp) || string.IsNullOrWhiteSpace(stamp))
            throw new InvalidDataException("Der F95-Thread wurde vom Update-Dienst nicht gefunden.");
        using JsonDocument full = await GetJsonAsync("full/" + threadId + "?ts=" + Uri.EscapeDataString(stamp), cancellationToken);
        JsonElement root = full.RootElement;
        ThrowIfIndexerError(root);
        string version = ReadString(root, "version")?.Trim() ?? "";
        if (version.Length == 0) throw new InvalidDataException("F95 hat keine Version für diesen Thread geliefert.");
        return new F95Metadata(
            threadId,
            ReadString(root, "name")?.Trim() ?? "",
            version,
            ReadString(root, "developer")?.Trim() ?? "",
            FormatStatus(ReadString(root, "status")),
            (ReadString(root, "image_url") ?? ReadString(root, "image"))?.Trim() ?? "",
            ParseUrlArray(ReadString(root,"previews_urls"))) { Tags = MetadataEnrichment.ReadTags(root), Description = ReadString(root,"description") ?? "", Engine = ReadString(root,"engine") ?? "" };
    }

    private static IReadOnlyList<string> ParseUrlArray(string? json)
    {
        if(string.IsNullOrWhiteSpace(json))return Array.Empty<string>();
        try
        {
            var values=JsonSerializer.Deserialize<List<string>>(json);
            return values?.Where(value=>
                Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme==Uri.UriSchemeHttps)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(40).ToList()??new List<string>();
        }
        catch(JsonException){return Array.Empty<string>();}
    }

    public async Task<List<string>> DownloadGalleryAsync(IEnumerable<string> urls,string folder,string threadId,CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        var result=new List<string>();int index=0;
        foreach(string url in urls.Distinct(StringComparer.OrdinalIgnoreCase).Take(40))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string? path=await DownloadImageAsync(url,folder,$"{threadId}-{index++:00}",cancellationToken);
                if(path!=null)result.Add(path);
            }
            catch(Exception ex) when(ex is not OperationCanceledException){System.Diagnostics.Debug.WriteLine("Galeriebild übersprungen: "+ex.Message);}
        }
        return result;
    }

    public async Task<string?> DownloadImageAsync(string imageUrl, string folder, string fileStem, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        using HttpResponseMessage response = await GetPublicImageResponseAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        const int maxImageBytes = 15 * 1024 * 1024;
        if (response.Content.Headers.ContentLength is > maxImageBytes) throw new InvalidDataException("Das F95-Bild ist zu groß.");
        string media = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
        if (media.Length > 0 && !media.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Der Update-Dienst hat kein Bild geliefert.");
        Directory.CreateDirectory(folder);
        string temporary = Path.Combine(folder, fileStem + ".download.tmp");
        try
        {
            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                byte[] buffer = new byte[81920];
                int total = 0, read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > maxImageBytes) throw new InvalidDataException("Das F95-Bild ist zu groß.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            string extension=DetectImageExtension(temporary,media);
            string destination=Path.Combine(folder,fileStem+extension);
            File.Move(temporary,destination,true);
            return destination;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string DetectImageExtension(string path,string media)
    {
        Span<byte> header=stackalloc byte[12];
        using(var file=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read))file.ReadExactly(header);
        if(header[..6].SequenceEqual("GIF87a"u8)||header[..6].SequenceEqual("GIF89a"u8))return ".gif";
        if(header[..8].SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))return ".png";
        if(header[..4].SequenceEqual("RIFF"u8)&&header[8..12].SequenceEqual("WEBP"u8))return ".webp";
        if(header[0]==0xff&&header[1]==0xd8)return ".jpg";
        return media switch{"image/gif"=>".gif","image/png"=>".png","image/webp"=>".webp",_=>".jpg"};
    }

    public async Task<F95UpdateReport> CheckAsync(IEnumerable<Game> sourceGames, CancellationToken cancellationToken)
    {
        var eligible = sourceGames
            .Where(g => g.Status is Constants.GameStatus.Installed or Constants.GameStatus.WaitingForUpdates)
            .Select(g => (Game: g, Valid: TryExtractThreadId(g.InstallSource, out string id), Id: id))
            .Where(x => x.Valid)
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .Select(group => (Id: group.Key, Games: group.Select(x => x.Game).ToList()))
            .ToList();

        int checkedCount = 0, missingVersion = 0, failed = 0;
        var newUpdates = new List<Game>();

        foreach (var batch in eligible.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Dictionary<string, string> changed;
            try
            {
                string ids = string.Join(',', batch.Select(x => x.Id));
                using JsonDocument json = await GetJsonAsync("fast?ids=" + Uri.EscapeDataString(ids), cancellationToken);
                changed = ParseFast(json.RootElement);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                foreach (var entry in batch)
                    foreach (Game game in entry.Games)
                    {
                        game.UpdateCheckError = FriendlyError(ex);
                        game.LastUpdateCheckUtc = DateTime.UtcNow;
                        failed++;
                    }
                continue;
            }

            foreach (var entry in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!changed.TryGetValue(entry.Id, out string? stamp) || string.IsNullOrWhiteSpace(stamp))
                {
                    foreach (Game game in entry.Games)
                    {
                        game.F95ThreadId = entry.Id;
                        game.UpdateCheckError = "Der F95-Thread wurde vom Update-Dienst nicht gefunden.";
                        game.LastUpdateCheckUtc = DateTime.UtcNow;
                        failed++;
                    }
                    continue;
                }
                bool mustLoadFull = entry.Games.Any(g => g.F95ThreadId != entry.Id ||
                    string.IsNullOrWhiteSpace(g.LatestKnownVersion) || g.F95LastChanged != stamp);
                string? onlineVersion = entry.Games.Select(g => g.LatestKnownVersion).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                try
                {
                    if (mustLoadFull)
                    {
                        using JsonDocument full = await GetJsonAsync("full/" + entry.Id + "?ts=" + Uri.EscapeDataString(stamp), cancellationToken);
                        ThrowIfIndexerError(full.RootElement);
                        onlineVersion = ReadString(full.RootElement, "version");
                        string developer = ReadString(full.RootElement, "developer")?.Trim() ?? "";
                        string status = FormatStatus(ReadString(full.RootElement, "status"));
                        if (string.IsNullOrWhiteSpace(onlineVersion))
                            throw new InvalidDataException("Die Online-Version fehlt in der Antwort.");
                        foreach (Game game in entry.Games)
                        {
                            MetadataEnrichment.Apply(game, new F95Metadata(entry.Id, "", onlineVersion ?? "", developer, status, "", Array.Empty<string>()) { Tags = MetadataEnrichment.ReadTags(full.RootElement), Description = ReadString(full.RootElement,"description") ?? "", Engine = ReadString(full.RootElement,"engine") ?? "" });
                            if (status.Length > 0) game.F95Status = status;
                        }
                    }

                    foreach (Game game in entry.Games)
                    {
                        string? previousOnlineVersion = NormalizeVersion(game.LatestKnownVersion);
                        game.F95ThreadId = entry.Id;
                        if (!string.IsNullOrWhiteSpace(onlineVersion)) game.LatestKnownVersion = onlineVersion.Trim();
                        if (changed.TryGetValue(entry.Id, out string? newStamp)) game.F95LastChanged = newStamp;
                        game.LastUpdateCheckUtc = DateTime.UtcNow;
                        game.UpdateCheckError = "";
                        game.UpdateAvailable = IsUpdateAvailable(game.GameVersion, game.LatestKnownVersion, game.IgnoredUpdateVersion);
                        if (NormalizeVersion(game.GameVersion) == null) missingVersion++;
                        if (game.UpdateAvailable && !StringComparer.OrdinalIgnoreCase.Equals(
                            previousOnlineVersion, NormalizeVersion(game.LatestKnownVersion))) newUpdates.Add(game);
                        checkedCount++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    foreach (Game game in entry.Games)
                    {
                        game.F95ThreadId = entry.Id;
                        game.UpdateCheckError = FriendlyError(ex);
                        game.LastUpdateCheckUtc = DateTime.UtcNow;
                        failed++;
                    }
                }
            }
        }

        return new F95UpdateReport(eligible.Sum(x => x.Games.Count), checkedCount,
            eligible.SelectMany(x => x.Games).Count(g => g.UpdateAvailable), missingVersion, failed, newUpdates);
    }

    private async Task<JsonDocument> GetJsonAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(relativeUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("Der Update-Dienst ist gerade ausgelastet.", null, response.StatusCode);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxPayloadBytes)
            throw new InvalidDataException("Die Antwort des Update-Dienstes ist zu groß.");
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var bounded = new MemoryStream();
        byte[] buffer = new byte[81920];
        int total = 0, read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaxPayloadBytes) throw new InvalidDataException("Die Antwort des Update-Dienstes ist zu groß.");
            await bounded.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        bounded.Position = 0;
        return await JsonDocument.ParseAsync(bounded, new JsonDocumentOptions { MaxDepth = 32 }, cancellationToken);
    }

    private static Dictionary<string, string> ParseFast(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Ungültige Update-Antwort.");
        ThrowIfIndexerError(root);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty item in root.EnumerateObject())
            if (Regex.IsMatch(item.Name, @"^[1-9]\d*$") &&
                long.TryParse(item.Value.ToString(), out long timestamp) && timestamp > 0)
                result[item.Name] = timestamp.ToString();
        return result;
    }

    private static void ThrowIfIndexerError(JsonElement root)
    {
        string? error = ReadString(root, "INDEX_ERROR")?.Trim();
        if (string.IsNullOrWhiteSpace(error)) return;
        if (error.Length > 180) error = error[..180] + "…";
        throw new InvalidDataException("F95-Indexer: " + error);
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        foreach (JsonProperty property in root.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
        return null;
    }

    private static bool IsF95Host(string host) => new[] { "f95zone.to", "f95zone.com", "f95zone.ninja" }
        .Any(domain => host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

    private async Task<HttpResponseMessage> GetPublicImageResponseAsync(Uri initial, CancellationToken cancellationToken)
    {
        Uri current = initial;
        for (int redirect = 0; redirect <= 3; redirect++)
        {
            await EnsurePublicHttpsAddressAsync(current, cancellationToken);
            HttpResponseMessage response = await imageClient.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location != null)
            {
                Uri next = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(current, response.Headers.Location);
                response.Dispose();
                current = next;
                continue;
            }
            return response;
        }
        throw new HttpRequestException("Zu viele Weiterleitungen beim F95-Bild.");
    }

    private static async Task EnsurePublicHttpsAddressAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.DnsSafeHost))
            throw new InvalidDataException("Unsichere Bildadresse.");
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(IsPrivateAddress))
            throw new InvalidDataException("Die Bildadresse verweist auf ein privates Netzwerk.");
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length == 16) return (bytes[0] & 0xFE) == 0xFC;
        return bytes[0] is 0 or 10 or 127 || bytes[0] >= 224 ||
            (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
            (bytes[0] == 169 && bytes[1] == 254) ||
            (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168) ||
            (bytes[0] == 198 && bytes[1] is 18 or 19);
    }

    private static string FormatStatus(string? value) => value?.Trim() switch
    {
        "1" => "Laufend",
        "2" => "Abgeschlossen",
        "3" => "Pausiert",
        "4" => "Abgebrochen",
        null or "" => "",
        _ => value.Trim()
    };

    private static string FriendlyError(Exception ex) => ex switch
    {
        TaskCanceledException => "Zeitüberschreitung beim Update-Dienst.",
        HttpRequestException request when request.StatusCode.HasValue =>
            $"Update-Dienst antwortet mit HTTP {(int)request.StatusCode.Value}.",
        HttpRequestException => "Keine Verbindung zum Update-Dienst.",
        JsonException => "Die Antwort des Update-Dienstes war ungültig.",
        _ => ex.Message
    };

    public void Dispose()
    {
        imageClient.Dispose();
        if (ownsClient) client.Dispose();
    }
}
