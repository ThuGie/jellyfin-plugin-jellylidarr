using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.JellyLidarr;

public sealed class ReconcileTask(IRequestService requests) : IScheduledTask
{
    public string Name => "Reconcile music requests";
    public string Key => "JellyLidarrReconcile";
    public string Description => "Updates JellyLidarr requests from Lidarr and the Jellyfin music library.";
    public string Category => "JellyLidarr";
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    { progress.Report(0); await requests.ReconcileAsync(cancellationToken).ConfigureAwait(false); progress.Report(100); }
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [new() { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromSeconds(Math.Max(15, Plugin.Instance?.Configuration.PollingSeconds ?? 60)).Ticks }];
}
