using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyLidarr;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RequestKind { Artist, Album }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RequestState { Pending, Approved, Searching, Downloading, Importing, Available, Failed, Rejected, Cancelled }

public sealed record MusicRequest(
    [property: JsonPropertyName("id")] long Id, [property: JsonPropertyName("userId")] Guid UserId, [property: JsonPropertyName("userName")] string UserName, [property: JsonPropertyName("kind")] RequestKind Kind, [property: JsonPropertyName("musicBrainzId")] string MusicBrainzId, [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("artistName")] string? ArtistName, [property: JsonPropertyName("lidarrArtistId")] int? LidarrArtistId, [property: JsonPropertyName("lidarrAlbumId")] int? LidarrAlbumId, [property: JsonPropertyName("state")] RequestState State,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt, [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt, [property: JsonPropertyName("approverId")] Guid? ApproverId, [property: JsonPropertyName("failureReason")] string? FailureReason, [property: JsonPropertyName("retryCount")] int RetryCount);

public sealed record AuditEvent([property: JsonPropertyName("id")] long Id, [property: JsonPropertyName("requestId")] long RequestId, [property: JsonPropertyName("actorId")] Guid ActorId, [property: JsonPropertyName("action")] string Action, [property: JsonPropertyName("detail")] string? Detail, [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);
public sealed record CreateRequestDto([property: JsonPropertyName("kind")] RequestKind Kind, [property: JsonPropertyName("musicBrainzId")] string MusicBrainzId, [property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("artistName")] string? ArtistName);
public sealed record RejectRequestDto([property: JsonPropertyName("reason")] string Reason);
public sealed record SearchResultDto([property: JsonPropertyName("kind")] RequestKind Kind, [property: JsonPropertyName("musicBrainzId")] string MusicBrainzId, [property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("artistName")] string? ArtistName,
    [property: JsonPropertyName("overview")] string? Overview, [property: JsonPropertyName("imageUrl")] string? ImageUrl, [property: JsonPropertyName("available")] bool Available, [property: JsonPropertyName("partial")] bool Partial, [property: JsonPropertyName("fallbackMatch")] bool FallbackMatch, [property: JsonPropertyName("jellyfinItemId")] Guid? JellyfinItemId,
    [property: JsonPropertyName("requestId")] long? RequestId, [property: JsonPropertyName("requestState")] RequestState? RequestState);
public sealed record AvailabilityDto([property: JsonPropertyName("musicBrainzId")] string MusicBrainzId, [property: JsonPropertyName("available")] bool Available, [property: JsonPropertyName("partial")] bool Partial, [property: JsonPropertyName("fallbackMatch")] bool FallbackMatch, [property: JsonPropertyName("jellyfinItemId")] Guid? JellyfinItemId);
public sealed record LidarrOptions([property: JsonPropertyName("rootFolders")] IReadOnlyList<LidarrOption> RootFolders, [property: JsonPropertyName("qualityProfiles")] IReadOnlyList<LidarrOption> QualityProfiles, [property: JsonPropertyName("metadataProfiles")] IReadOnlyList<LidarrOption> MetadataProfiles);
public sealed record LidarrOption([property: JsonPropertyName("id")] int Id, [property: JsonPropertyName("name")] string Name);
public sealed record CurrentUser([property: JsonPropertyName("id")] Guid Id, [property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("role")] UserRole Role, [property: JsonPropertyName("isAdministrator")] bool IsAdministrator);
