using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyLidarr;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static readonly Guid PluginGuid = Guid.Parse("f54e9ef8-89df-4eb8-a734-7f81b516ddce");
    public static Plugin? Instance { get; private set; }

    public Plugin(IApplicationPaths paths, IXmlSerializer serializer) : base(paths, serializer) => Instance = this;
    public override string Name => "JellyLidarr";
    public override Guid Id => PluginGuid;
    public override string Description => "Discover and request music from Lidarr without leaving Jellyfin.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            DisplayName = "JellyLidarr",
            EmbeddedResourcePath = $"{GetType().Namespace}.Web.admin.html",
            EnableInMainMenu = true,
            MenuSection = "server",
            MenuIcon = "library_music"
        };
        yield return new PluginPageInfo { Name = "JellyLidarrPortal", EmbeddedResourcePath = $"{GetType().Namespace}.Web.portal.html" };
        yield return new PluginPageInfo { Name = "jellylidarr-app", EmbeddedResourcePath = $"{GetType().Namespace}.Web.app.js" };
        yield return new PluginPageInfo { Name = "jellylidarr-style", EmbeddedResourcePath = $"{GetType().Namespace}.Web.style.css" };
    }
}

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public string LidarrUrl { get; set; } = "http://localhost:8686";
    public string LidarrApiKey { get; set; } = string.Empty;
    public int RootFolderId { get; set; }
    public int QualityProfileId { get; set; }
    public int MetadataProfileId { get; set; }
    public string MonitorMode { get; set; } = "all";
    public int PollingSeconds { get; set; } = 60;
    public int ImportTimeoutHours { get; set; } = 24;
    public UserRoleAssignment[] UserRoles { get; set; } = Array.Empty<UserRoleAssignment>();

    public UserRole RoleFor(string userId) => UserRoles.FirstOrDefault(x => string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase))?.Role ?? UserRole.Viewer;
}

public sealed class UserRoleAssignment
{
    public string UserId { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Viewer;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserRole { Viewer, Requester, TrustedRequester, Approver }
