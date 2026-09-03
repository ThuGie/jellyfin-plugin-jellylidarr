using System.Text.Json;

namespace Jellyfin.Plugin.JellyLidarr;

public static class LidarrArtwork
{
    public static string? Read(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array) return null;
        string? fallback = null;
        foreach (var image in images.EnumerateArray())
        {
            if (image.ValueKind != JsonValueKind.Object) continue;
            foreach (var property in new[] { "remoteUrl", "url" })
            {
                if (!image.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) continue;
                var text = value.GetString();
                if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) continue;
                if (image.TryGetProperty("coverType", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() is "cover" or "poster") return text;
                fallback ??= text;
            }
        }
        return fallback;
    }
}
