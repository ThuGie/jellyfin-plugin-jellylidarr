using System.Net.Http.Json;
using System.Text.Json;

namespace Jellyfin.Plugin.JellyLidarr;

public interface ILidarrClient
{
    Task<IReadOnlyList<SearchResultDto>> SearchAsync(string term, RequestKind? kind, int limit, CancellationToken token);
    Task<LidarrOptions> GetOptionsAsync(CancellationToken token);
    Task<(int ArtistId, int? AlbumId)> DispatchAsync(MusicRequest request, CancellationToken token);
    Task<RequestState> GetStateAsync(MusicRequest request, CancellationToken token);
}

public sealed class LidarrClient(HttpClient http) : ILidarrClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<SearchResultDto>> SearchAsync(string term, RequestKind? kind, int limit, CancellationToken token)
    {
        EnsureConfigured();
        var results = new List<SearchResultDto>();
        if (kind is null or RequestKind.Artist)
        {
            using var doc = await GetAsync($"api/v1/artist/lookup?term={Uri.EscapeDataString(term)}", token).ConfigureAwait(false);
            foreach (var item in doc.RootElement.EnumerateArray().Take(limit))
            {
                var mbid = String(item, "foreignArtistId"); if (string.IsNullOrWhiteSpace(mbid)) continue;
                results.Add(new(RequestKind.Artist, mbid, String(item, "artistName") ?? "Unknown artist", null,
                    String(item, "overview"), Image(item), false, false, false, null, null, null));
            }
        }
        if (kind is null or RequestKind.Album)
        {
            using var doc = await GetAsync($"api/v1/album/lookup?term={Uri.EscapeDataString(term)}", token).ConfigureAwait(false);
            foreach (var item in doc.RootElement.EnumerateArray().Take(limit))
            {
                var mbid = String(item, "foreignAlbumId"); if (string.IsNullOrWhiteSpace(mbid)) continue;
                var artist = item.TryGetProperty("artist", out var a) ? String(a, "artistName") : null;
                results.Add(new(RequestKind.Album, mbid, String(item, "title") ?? "Unknown album", artist,
                    String(item, "overview"), Image(item), false, false, false, null, null, null));
            }
        }
        return results.Take(limit).ToArray();
    }

    public async Task<LidarrOptions> GetOptionsAsync(CancellationToken token)
    {
        EnsureConfigured();
        var roots = await Options("api/v1/rootfolder", "path", token).ConfigureAwait(false);
        var quality = await Options("api/v1/qualityprofile", "name", token).ConfigureAwait(false);
        var metadata = await Options("api/v1/metadataprofile", "name", token).ConfigureAwait(false);
        return new(roots, quality, metadata);
    }

    public async Task<(int ArtistId, int? AlbumId)> DispatchAsync(MusicRequest request, CancellationToken token)
    {
        var cfg = EnsureConfigured();
        if (request.Kind == RequestKind.Artist)
        {
            var lookup = await FirstLookup($"api/v1/artist/lookup?term=lidarr:{request.MusicBrainzId}", token).ConfigureAwait(false);
            var payload = new Dictionary<string, object?>
            {
                ["foreignArtistId"] = request.MusicBrainzId, ["artistName"] = request.Name,
                ["qualityProfileId"] = cfg.QualityProfileId, ["metadataProfileId"] = cfg.MetadataProfileId,
                ["rootFolderPath"] = await RootPath(cfg.RootFolderId, token).ConfigureAwait(false), ["monitored"] = true,
                ["monitorNewItems"] = cfg.MonitorMode, ["addOptions"] = new { monitor = cfg.MonitorMode, searchForMissingAlbums = true }
            };
            Copy(lookup, payload, "images", "genres", "sortName", "status", "overview", "artistType", "disambiguation");
            var created = await PostAsync("api/v1/artist", payload, token).ConfigureAwait(false);
            return (created.GetProperty("id").GetInt32(), null);
        }

        var album = await FirstLookup($"api/v1/album/lookup?term=lidarr:{request.MusicBrainzId}", token).ConfigureAwait(false);
        var artistElement = album.GetProperty("artist");
        var artistMbid = String(artistElement, "foreignArtistId") ?? throw new InvalidOperationException("Lidarr returned an album without an artist ID.");
        var artistId = await EnsureArtistAsync(artistElement, artistMbid, cfg, token).ConfigureAwait(false);
        var albumPayload = JsonSerializer.Deserialize<Dictionary<string, object?>>(album.GetRawText(), Json)!;
        albumPayload["artistId"] = artistId; albumPayload["monitored"] = true;
        albumPayload["addOptions"] = new { searchForNewAlbum = true };
        var createdAlbum = await PostAsync("api/v1/album", albumPayload, token).ConfigureAwait(false);
        return (artistId, createdAlbum.GetProperty("id").GetInt32());
    }

    public async Task<RequestState> GetStateAsync(MusicRequest request, CancellationToken token)
    {
        EnsureConfigured();
        using var queue = await GetAsync("api/v1/queue?page=1&pageSize=100&includeUnknownArtistItems=true", token).ConfigureAwait(false);
        var records = queue.RootElement.TryGetProperty("records", out var rs) ? rs.EnumerateArray() : queue.RootElement.EnumerateArray();
        foreach (var entry in records)
        {
            var artistId = Int(entry, "artistId"); var albumId = Int(entry, "albumId");
            if ((request.LidarrAlbumId.HasValue && albumId == request.LidarrAlbumId) || (!request.LidarrAlbumId.HasValue && artistId == request.LidarrArtistId))
                return RequestState.Downloading;
        }
        if (request.LidarrAlbumId.HasValue)
        {
            using var album = await GetAsync($"api/v1/album/{request.LidarrAlbumId.Value}", token).ConfigureAwait(false);
            if (album.RootElement.TryGetProperty("statistics", out var stats) && Int(stats, "percentOfTracks") >= 100) return RequestState.Importing;
        }
        else if (request.LidarrArtistId.HasValue)
        {
            using var artist = await GetAsync($"api/v1/artist/{request.LidarrArtistId.Value}", token).ConfigureAwait(false);
            if (artist.RootElement.TryGetProperty("statistics", out var stats) && Int(stats, "percentOfTracks") >= 100) return RequestState.Importing;
        }
        return RequestState.Searching;
    }

    private async Task<int> EnsureArtistAsync(JsonElement artist, string mbid, PluginConfiguration cfg, CancellationToken token)
    {
        using var existing = await GetAsync("api/v1/artist", token).ConfigureAwait(false);
        foreach (var item in existing.RootElement.EnumerateArray()) if (String(item, "foreignArtistId") == mbid) return item.GetProperty("id").GetInt32();
        var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(artist.GetRawText(), Json)!;
        payload["qualityProfileId"] = cfg.QualityProfileId; payload["metadataProfileId"] = cfg.MetadataProfileId;
        payload["rootFolderPath"] = await RootPath(cfg.RootFolderId, token).ConfigureAwait(false); payload["monitored"] = false;
        payload["addOptions"] = new { monitor = "none", searchForMissingAlbums = false };
        return (await PostAsync("api/v1/artist", payload, token).ConfigureAwait(false)).GetProperty("id").GetInt32();
    }

    private async Task<string> RootPath(int id, CancellationToken token)
    {
        using var roots = await GetAsync("api/v1/rootfolder", token).ConfigureAwait(false);
        return roots.RootElement.EnumerateArray().First(x => Int(x, "id") == id).GetProperty("path").GetString()!;
    }
    private async Task<JsonElement> FirstLookup(string path, CancellationToken token)
    { using var doc = await GetAsync(path, token).ConfigureAwait(false); return doc.RootElement.EnumerateArray().FirstOrDefault().Clone(); }
    private async Task<IReadOnlyList<LidarrOption>> Options(string path, string label, CancellationToken token)
    { using var doc = await GetAsync(path, token).ConfigureAwait(false); return doc.RootElement.EnumerateArray().Select(x => new LidarrOption(Int(x,"id"), String(x,label) ?? $"#{Int(x,"id")}")).ToArray(); }
    private async Task<JsonDocument> GetAsync(string path, CancellationToken token)
    { using var req = Request(HttpMethod.Get, path); using var response = await http.SendAsync(req, token).ConfigureAwait(false); response.EnsureSuccessStatusCode(); return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token).ConfigureAwait(false); }
    private async Task<JsonElement> PostAsync(string path, object payload, CancellationToken token)
    { using var req = Request(HttpMethod.Post, path); req.Content = JsonContent.Create(payload, options: Json); using var response = await http.SendAsync(req, token).ConfigureAwait(false); response.EnsureSuccessStatusCode(); using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token).ConfigureAwait(false); return doc.RootElement.Clone(); }
    private static void Copy(JsonElement source, IDictionary<string,object?> target, params string[] names) { foreach (var name in names) if (source.TryGetProperty(name, out var value)) target[name] = value.Clone(); }
    private HttpRequestMessage Request(HttpMethod method, string path) { var cfg = EnsureConfigured(); var req = new HttpRequestMessage(method, new Uri(new Uri(cfg.LidarrUrl.TrimEnd('/') + "/"), path)); req.Headers.Add("X-Api-Key", cfg.LidarrApiKey); return req; }
    private static PluginConfiguration EnsureConfigured() { var cfg = Plugin.Instance?.Configuration ?? throw new InvalidOperationException("Plugin is unavailable."); if (!Uri.TryCreate(cfg.LidarrUrl, UriKind.Absolute, out _) || string.IsNullOrWhiteSpace(cfg.LidarrApiKey)) throw new InvalidOperationException("Lidarr is not configured."); return cfg; }
    private static string? String(JsonElement x, string name) => x.ValueKind == JsonValueKind.Object && x.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int Int(JsonElement x, string name) => x.ValueKind == JsonValueKind.Object && x.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
    private static string? Image(JsonElement x) => LidarrArtwork.Read(x);
}
