using System.Security.Claims;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Jellyfin.Data.Enums;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;

namespace Jellyfin.Plugin.JellyLidarr;

public interface IUserContext { CurrentUser Get(); }
public sealed class JellyfinUserContext(IHttpContextAccessor context, IUserManager users) : IUserContext
{
    public CurrentUser Get()
    {
        var principal = context.HttpContext?.User ?? throw new UnauthorizedAccessException();
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("Jellyfin-UserId") ?? principal.FindFirstValue("userId");
        if (!Guid.TryParse(raw, out var id)) throw new UnauthorizedAccessException();
        var user = users.GetUserById(id) ?? throw new UnauthorizedAccessException();
        var admin = user.HasPermission(PermissionKind.IsAdministrator);
        var role = admin ? UserRole.Approver : Plugin.Instance?.Configuration.UserRoles.GetValueOrDefault(id.ToString(), UserRole.Viewer) ?? UserRole.Viewer;
        return new(id, user.Username, role, admin);
    }
}

public interface IAvailabilityService { Task<AvailabilityDto> GetAsync(string mbid, RequestKind kind, string? name, string? artist, CancellationToken token); }
public sealed class JellyfinAvailabilityService(ILibraryManager library) : IAvailabilityService
{
    public Task<AvailabilityDto> GetAsync(string mbid, RequestKind kind, string? name, string? artist, CancellationToken token)
    {
        var wanted = kind == RequestKind.Artist ? BaseItemKind.MusicArtist : BaseItemKind.MusicAlbum;
        var items = library.GetItemList(new InternalItemsQuery { IncludeItemTypes = [wanted], Recursive = true });
        var exact = items.FirstOrDefault(x => x.ProviderIds.Any(p => p.Key.Contains("MusicBrainz", StringComparison.OrdinalIgnoreCase) && string.Equals(p.Value, mbid, StringComparison.OrdinalIgnoreCase)));
        if (exact is not null) return Task.FromResult(new AvailabilityDto(mbid, true, kind == RequestKind.Artist, false, exact.Id));
        var fallback = items.FirstOrDefault(x => Normalize(x.Name) == Normalize(name));
        return Task.FromResult(new AvailabilityDto(mbid, fallback is not null, false, fallback is not null, fallback?.Id));
    }
    private static string Normalize(string? value) => new((value ?? string.Empty).Normalize().Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
