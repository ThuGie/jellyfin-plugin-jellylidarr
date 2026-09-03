using Xunit;
using System.Xml.Serialization;
using System.Text.Json;

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

    [Fact]
    public void Plugin_configuration_is_xml_serializable()
    {
        var expected = new PluginConfiguration
        {
            UserRoles = [new UserRoleAssignment { UserId = "user-1", Role = UserRole.Requester }]
        };
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var stream = new MemoryStream();
        serializer.Serialize(stream, expected);
        stream.Position = 0;
        var actual = Assert.IsType<PluginConfiguration>(serializer.Deserialize(stream));
        Assert.Equal(UserRole.Requester, actual.RoleFor("USER-1"));
    }

    private static CurrentUser User(UserRole role, bool admin = false) => new(Guid.NewGuid(), "test", role, admin);

    [Fact]
    public void Options_use_browser_names_even_without_camel_case_server_policy()
    {
        var options = new LidarrOptions([new(1, "/music")], [new(2, "Lossless")], [new(3, "Standard")]);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(options));
        Assert.Equal("/music", json.RootElement.GetProperty("rootFolders")[0].GetProperty("name").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("qualityProfiles")[0].GetProperty("id").GetInt32());
        Assert.Equal("Standard", json.RootElement.GetProperty("metadataProfiles")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void User_names_and_permissions_use_browser_contract()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(User(UserRole.Approver, true)));
        Assert.Equal("test", json.RootElement.GetProperty("name").GetString());
        Assert.True(json.RootElement.GetProperty("isAdministrator").GetBoolean());
        Assert.Equal("Approver", json.RootElement.GetProperty("role").GetString());
    }

    [Fact]
    public void Every_browser_dto_property_has_an_explicit_json_name()
    {
        Type[] types = [typeof(LidarrOptions), typeof(LidarrOption), typeof(CurrentUser), typeof(ConfigurationDto), typeof(MusicRequest), typeof(SearchResultDto), typeof(AvailabilityDto)];
        foreach (var type in types)
            foreach (var property in type.GetProperties())
                Assert.NotEmpty(property.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), false));
    }
}
