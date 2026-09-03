using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;

namespace Jellyfin.Plugin.JellyLidarr;

[ApiController, Authorize, Route("JellyLidarr")]
public sealed class JellyLidarrController(ILidarrClient lidarr, IAvailabilityService availability, RequestRepository repository, IRequestService requests, IUserContext users) : ControllerBase
{
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<SearchResultDto>>> Search([FromQuery] string term, [FromQuery] RequestKind? type, [FromQuery] int limit = 30, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Length < 2) return BadRequest("Enter at least two characters.");
        var found = await lidarr.SearchAsync(term.Trim(), type, Math.Clamp(limit, 1, 50), token).ConfigureAwait(false);
        var active = await repository.ListAsync(null, token).ConfigureAwait(false);
        var result = new List<SearchResultDto>(found.Count);
        foreach (var item in found)
        {
            var owned = await availability.GetAsync(item.MusicBrainzId, item.Kind, item.Name, item.ArtistName, token).ConfigureAwait(false);
            var request = active.FirstOrDefault(x => x.Kind == item.Kind && x.MusicBrainzId.Equals(item.MusicBrainzId, StringComparison.OrdinalIgnoreCase) && x.State is not (RequestState.Rejected or RequestState.Cancelled));
            result.Add(item with { Available = owned.Available, Partial = owned.Partial, FallbackMatch = owned.FallbackMatch, JellyfinItemId = owned.JellyfinItemId, RequestId = request?.Id, RequestState = request?.State });
        }
        return Ok(result);
    }

    [HttpGet("availability/{musicBrainzId}")]
    public async Task<ActionResult<AvailabilityDto>> Availability(string musicBrainzId, [FromQuery] RequestKind type, CancellationToken token)
        => Ok(await availability.GetAsync(musicBrainzId, type, null, null, token).ConfigureAwait(false));

    [HttpGet("requests")]
    public async Task<ActionResult<IReadOnlyList<MusicRequest>>> List([FromQuery] bool all = false, CancellationToken token = default)
    {
        var user = users.Get();
        if (all && !(user.IsAdministrator || user.Role >= UserRole.Approver)) return Forbid();
        return Ok(await repository.ListAsync(all ? null : user.Id, token).ConfigureAwait(false));
    }

    [HttpGet("requests/{id:long}")]
    public async Task<ActionResult<MusicRequest>> Get(long id, CancellationToken token)
    {
        var user = users.Get(); var item = await repository.GetAsync(id, token).ConfigureAwait(false);
        if (item is null) return NotFound();
        return item.UserId == user.Id || user.IsAdministrator || user.Role >= UserRole.Approver ? Ok(item) : Forbid();
    }

    [HttpPost("requests")]
    public async Task<ActionResult<MusicRequest>> Create(CreateRequestDto input, CancellationToken token)
    {
        try { var result = await requests.CreateAsync(input, users.Get(), token).ConfigureAwait(false); return CreatedAtAction(nameof(Get), new { id = result.Id }, result); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    [HttpPost("requests/{id:long}/approve")] public Task<ActionResult<MusicRequest>> Approve(long id, CancellationToken token) => Act(id, "approve", null, token);
    [HttpPost("requests/{id:long}/cancel")] public Task<ActionResult<MusicRequest>> Cancel(long id, CancellationToken token) => Act(id, "cancel", null, token);
    [HttpPost("requests/{id:long}/retry")] public Task<ActionResult<MusicRequest>> Retry(long id, CancellationToken token) => Act(id, "retry", null, token);
    [HttpPost("requests/{id:long}/reject")] public Task<ActionResult<MusicRequest>> Reject(long id, RejectRequestDto input, CancellationToken token) => Act(id, "reject", input.Reason, token);

    [HttpGet("me")] public ActionResult<CurrentUser> Me() => Ok(users.Get());

    private async Task<ActionResult<MusicRequest>> Act(long id, string action, string? reason, CancellationToken token)
    {
        try { return Ok(await requests.ActAsync(id, action, reason, users.Get(), token).ConfigureAwait(false)); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }
}

public sealed record ConfigurationDto(string LidarrUrl, string? LidarrApiKey, bool HasApiKey, int RootFolderId, int QualityProfileId,
    int MetadataProfileId, string MonitorMode, int PollingSeconds, int ImportTimeoutHours, Dictionary<string, UserRole> UserRoles);

[ApiController, Authorize, Route("JellyLidarr/settings")]
public sealed class JellyLidarrSettingsController(ILidarrClient lidarr, IUserContext users, IUserManager userManager) : ControllerBase
{
    [HttpGet]
    public ActionResult<ConfigurationDto> Get()
    {
        if (!users.Get().IsAdministrator) return Forbid(); var c = Plugin.Instance!.Configuration;
        return Ok(new ConfigurationDto(c.LidarrUrl, null, !string.IsNullOrEmpty(c.LidarrApiKey), c.RootFolderId, c.QualityProfileId, c.MetadataProfileId, c.MonitorMode, c.PollingSeconds, c.ImportTimeoutHours,
            c.UserRoles.ToDictionary(x => x.UserId, x => x.Role, StringComparer.OrdinalIgnoreCase)));
    }

    [HttpPut]
    public async Task<IActionResult> Put(ConfigurationDto input, CancellationToken token)
    {
        if (!users.Get().IsAdministrator) return Forbid();
        if (!Uri.TryCreate(input.LidarrUrl, UriKind.Absolute, out _) || input.PollingSeconds is < 15 or > 3600 || input.ImportTimeoutHours is < 1 or > 168) return BadRequest("Invalid URL, polling interval, or timeout.");
        var c = Plugin.Instance!.Configuration;
        var oldUrl = c.LidarrUrl; var oldKey = c.LidarrApiKey;
        c.LidarrUrl = input.LidarrUrl.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(input.LidarrApiKey)) c.LidarrApiKey = input.LidarrApiKey.Trim();
        try { await lidarr.GetOptionsAsync(token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        { c.LidarrUrl = oldUrl; c.LidarrApiKey = oldKey; return BadRequest($"Lidarr validation failed: {ex.Message}"); }
        c.RootFolderId = input.RootFolderId; c.QualityProfileId = input.QualityProfileId; c.MetadataProfileId = input.MetadataProfileId;
        c.MonitorMode = input.MonitorMode; c.PollingSeconds = input.PollingSeconds; c.ImportTimeoutHours = input.ImportTimeoutHours;
        c.UserRoles = input.UserRoles.Select(x => new UserRoleAssignment { UserId = x.Key, Role = x.Value }).ToArray();
        Plugin.Instance.SaveConfiguration();
        return Ok(new { success = true });
    }

    [HttpGet("options")]
    public async Task<ActionResult<LidarrOptions>> Options(CancellationToken token)
    { if (!users.Get().IsAdministrator) return Forbid(); return Ok(await lidarr.GetOptionsAsync(token).ConfigureAwait(false)); }

    [HttpGet("users")]
    public IActionResult Users()
    {
        if (!users.Get().IsAdministrator) return Forbid();
        return Ok(userManager.GetUsers().Select(x => new { x.Id, Name = x.Username, IsAdministrator = x.HasPermission(PermissionKind.IsAdministrator) }));
    }
}
