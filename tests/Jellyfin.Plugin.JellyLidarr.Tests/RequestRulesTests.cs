using Xunit;

namespace Jellyfin.Plugin.JellyLidarr.Tests;

public sealed class RequestRulesTests
{
    [Theory]
    [InlineData(RequestState.Pending, true)]
    [InlineData(RequestState.Downloading, true)]
    [InlineData(RequestState.Available, false)]
    [InlineData(RequestState.Failed, false)]
    [InlineData(RequestState.Cancelled, false)]
    public void Active_states_are_explicit(RequestState state, bool expected) => Assert.Equal(expected, RequestRules.IsActive(state));

    [Fact]
    public void Viewer_cannot_request_but_trusted_user_can()
    {
        Assert.False(RequestRules.CanRequest(User(UserRole.Viewer)));
        Assert.True(RequestRules.CanRequest(User(UserRole.TrustedRequester)));
    }

    [Fact]
    public void Only_approvers_and_admins_can_approve()
    {
        Assert.False(RequestRules.CanApprove(User(UserRole.TrustedRequester)));
        Assert.True(RequestRules.CanApprove(User(UserRole.Approver)));
        Assert.True(RequestRules.CanApprove(User(UserRole.Viewer, true)));
    }

    private static CurrentUser User(UserRole role, bool admin = false) => new(Guid.NewGuid(), "test", role, admin);
}
