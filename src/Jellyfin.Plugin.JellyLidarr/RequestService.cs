namespace Jellyfin.Plugin.JellyLidarr;

public interface IRequestService
{
    Task<MusicRequest> CreateAsync(CreateRequestDto input, CurrentUser user, CancellationToken token);
    Task<MusicRequest> ActAsync(long id, string action, string? reason, CurrentUser user, CancellationToken token);
    Task ReconcileAsync(CancellationToken token);
}

public sealed class RequestService(RequestRepository repository, ILidarrClient lidarr, IAvailabilityService availability) : IRequestService
{
    public async Task<MusicRequest> CreateAsync(CreateRequestDto input, CurrentUser user, CancellationToken token)
    {
        if (user.Role < UserRole.Requester && !user.IsAdministrator) throw new UnauthorizedAccessException("You do not have permission to request music.");
        if (!Guid.TryParse(input.MusicBrainzId, out _) || string.IsNullOrWhiteSpace(input.Name)) throw new ArgumentException("A valid MusicBrainz ID and name are required.");
        var owned = await availability.GetAsync(input.MusicBrainzId, input.Kind, input.Name, input.ArtistName, token).ConfigureAwait(false);
        if (owned.Available && !owned.Partial) throw new InvalidOperationException("This music is already available in Jellyfin.");
        var immediate = user.IsAdministrator || user.Role >= UserRole.TrustedRequester;
        var result = await repository.CreateOrGetAsync(input, user, immediate ? RequestState.Approved : RequestState.Pending, token).ConfigureAwait(false);
        if (immediate && result.State == RequestState.Approved) await DispatchAsync(result, user.Id, token).ConfigureAwait(false);
        return await repository.GetAsync(result.Id, token).ConfigureAwait(false) ?? result;
    }

    public async Task<MusicRequest> ActAsync(long id, string action, string? reason, CurrentUser user, CancellationToken token)
    {
        var request = await repository.GetAsync(id, token).ConfigureAwait(false) ?? throw new KeyNotFoundException();
        var owns = request.UserId == user.Id;
        if (action is "approve" or "reject" or "retry" && !(user.IsAdministrator || user.Role >= UserRole.Approver)) throw new UnauthorizedAccessException();
        if (action == "cancel" && !(owns || user.IsAdministrator || user.Role >= UserRole.Approver)) throw new UnauthorizedAccessException();
        switch (action)
        {
            case "approve":
                if (request.State != RequestState.Pending) throw new InvalidOperationException("Only pending requests can be approved.");
                await repository.UpdateAsync(id, RequestState.Approved, user.Id, "approved", null, null, null, false, token).ConfigureAwait(false);
                await DispatchAsync((await repository.GetAsync(id, token).ConfigureAwait(false))!, user.Id, token).ConfigureAwait(false); break;
            case "reject":
                if (request.State != RequestState.Pending || string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("A pending request and rejection reason are required.");
                await repository.UpdateAsync(id, RequestState.Rejected, user.Id, "rejected", reason.Trim(), null, null, false, token).ConfigureAwait(false); break;
            case "cancel":
                if (request.State is RequestState.Available or RequestState.Rejected or RequestState.Cancelled) throw new InvalidOperationException("This request can no longer be cancelled.");
                await repository.UpdateAsync(id, RequestState.Cancelled, user.Id, "cancelled", null, null, null, false, token).ConfigureAwait(false); break;
            case "retry":
                if (request.State != RequestState.Failed) throw new InvalidOperationException("Only failed requests can be retried.");
                if (request.RetryCount >= 3) throw new InvalidOperationException("This request has reached the three-retry limit.");
                await repository.UpdateAsync(id, RequestState.Approved, user.Id, "retried", null, null, null, true, token).ConfigureAwait(false);
                await DispatchAsync((await repository.GetAsync(id, token).ConfigureAwait(false))!, user.Id, token).ConfigureAwait(false); break;
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
        return (await repository.GetAsync(id, token).ConfigureAwait(false))!;
    }

    public async Task ReconcileAsync(CancellationToken token)
    {
        foreach (var request in await repository.ListAsync(null, token).ConfigureAwait(false))
        {
            if (request.State is not (RequestState.Searching or RequestState.Downloading or RequestState.Importing)) continue;
            try
            {
                var owned = await availability.GetAsync(request.MusicBrainzId, request.Kind, request.Name, request.ArtistName, token).ConfigureAwait(false);
                if (owned.Available && (!owned.Partial || request.State == RequestState.Importing)) { await repository.UpdateAsync(request.Id, RequestState.Available, Guid.Empty, "available", null, null, null, false, token).ConfigureAwait(false); continue; }
                if (DateTimeOffset.UtcNow - request.UpdatedAt > TimeSpan.FromHours(Plugin.Instance?.Configuration.ImportTimeoutHours ?? 24) && request.State == RequestState.Importing)
                    await repository.UpdateAsync(request.Id, RequestState.Failed, Guid.Empty, "timed-out", "Import timed out before appearing in Jellyfin.", null, null, false, token).ConfigureAwait(false);
                else
                {
                    var state = await lidarr.GetStateAsync(request, token).ConfigureAwait(false);
                    if (state != request.State) await repository.UpdateAsync(request.Id, state, Guid.Empty, "reconciled", null, null, null, false, token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { /* transient: retry next run */ }
        }
    }

    private async Task DispatchAsync(MusicRequest request, Guid actor, CancellationToken token)
    {
        try
        {
            var ids = await lidarr.DispatchAsync(request, token).ConfigureAwait(false);
            await repository.UpdateAsync(request.Id, RequestState.Searching, actor, "dispatched", null, ids.ArtistId, ids.AlbumId, false, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            await repository.UpdateAsync(request.Id, RequestState.Failed, actor, "dispatch-failed", ex.Message, null, null, false, token).ConfigureAwait(false);
        }
    }
}
