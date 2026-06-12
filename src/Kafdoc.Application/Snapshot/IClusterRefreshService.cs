using FluentResults;

namespace Kafdoc.Application.Snapshot;

/// <summary>Refreshes the in-memory cluster snapshot from Kafka.</summary>
public interface IClusterRefreshService
{
    /// <summary>
    /// Reads the cluster, builds the graph, and swaps it into the snapshot store.
    /// On failure the previous snapshot is left intact and the error is recorded.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the refresh.</param>
    /// <returns>A success result, or a failure describing what went wrong.</returns>
    Task<Result> RefreshAsync(CancellationToken cancellationToken);
}
