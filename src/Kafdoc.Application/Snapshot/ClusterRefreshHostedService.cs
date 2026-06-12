using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kafdoc.Application.Snapshot;

/// <summary>
/// Refreshes the cluster snapshot once at startup, then on a fixed interval.
/// A failed refresh is logged and leaves the previous snapshot serving.
/// </summary>
internal sealed partial class ClusterRefreshHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<RefreshOptions> options,
    ILogger<ClusterRefreshHostedService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshOnceAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(options.Value.RefreshInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RefreshOnceAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        // The refresh service depends on the singleton store but is itself resolved
        // per cycle so the reader/admin client lifetime stays well-defined.
        await using var scope = scopeFactory.CreateAsyncScope();
        var refresh = scope.ServiceProvider.GetRequiredService<IClusterRefreshService>();
        var result = await refresh.RefreshAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsFailed)
        {
            LogRefreshFailed(logger, string.Join("; ", result.Errors.Select(e => e.Message)));
        }
        else
        {
            LogRefreshSucceeded(logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Cluster refresh failed: {Errors}")]
    private static partial void LogRefreshFailed(ILogger logger, string errors);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cluster snapshot refreshed.")]
    private static partial void LogRefreshSucceeded(ILogger logger);
}
