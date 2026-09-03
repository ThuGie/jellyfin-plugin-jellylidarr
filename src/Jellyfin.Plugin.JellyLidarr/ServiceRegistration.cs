using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.JellyLidarr;

public sealed class ServiceRegistration : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddHttpClient<ILidarrClient, LidarrClient>();
        services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, NavigationStartupFilter>();
        services.AddSingleton<RequestRepository>();
        services.AddSingleton<IAvailabilityService, JellyfinAvailabilityService>();
        services.AddSingleton<IRequestService, RequestService>();
        services.AddSingleton<IUserContext, JellyfinUserContext>();
        services.AddSingleton<IScheduledTask, ReconcileTask>();
    }
}
