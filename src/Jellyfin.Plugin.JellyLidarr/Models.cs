using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyLidarr;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RequestKind { Artist, Album }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RequestState { Pending, Approved, Searching, Downloading, Importing, Available, Failed, Rejected, Cancelled }

public sealed record MusicRequest(
    long Id, Guid UserId, string UserName, RequestKind Kind, string MusicBrainzId, string Name,
    string? ArtistName, int? LidarrArtistId, int? LidarrAlbumId, RequestState State,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, Guid? ApproverId, string? FailureReason, int RetryCount);

public sealed record AuditEvent(long Id, long RequestId, Guid ActorId, string Action, string? Detail, DateTimeOffset CreatedAt);
public sealed record CreateRequestDto(RequestKind Kind, string MusicBrainzId, string Name, string? ArtistName);
public sealed record RejectRequestDto(string Reason);
public sealed record SearchResultDto(RequestKind Kind, string MusicBrainzId, string Name, string? ArtistName,
    string? Overview, string? ImageUrl, bool Available, bool Partial, bool FallbackMatch, Guid? JellyfinItemId,
    long? RequestId, RequestState? RequestState);
public sealed record AvailabilityDto(string MusicBrainzId, bool Available, bool Partial, bool FallbackMatch, Guid? JellyfinItemId);
public sealed record LidarrOptions(IReadOnlyList<LidarrOption> RootFolders, IReadOnlyList<LidarrOption> QualityProfiles, IReadOnlyList<LidarrOption> MetadataProfiles);
public sealed record LidarrOption(int Id, string Name);
public sealed record CurrentUser(Guid Id, string Name, UserRole Role, bool IsAdministrator);
