namespace Jellyfin.Plugin.JellyLidarr;

public static class RequestRules
{
    public static bool IsActive(RequestState state) => state is RequestState.Pending or RequestState.Approved or RequestState.Searching or RequestState.Downloading or RequestState.Importing;
    public static bool CanRequest(CurrentUser user) => user.IsAdministrator || user.Role >= UserRole.Requester;
    public static bool CanApprove(CurrentUser user) => user.IsAdministrator || user.Role >= UserRole.Approver;
    public static bool CanCancel(CurrentUser user, MusicRequest request) => user.IsAdministrator || user.Role >= UserRole.Approver || user.Id == request.UserId;
}
