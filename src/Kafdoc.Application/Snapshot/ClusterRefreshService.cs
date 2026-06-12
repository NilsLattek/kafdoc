using FluentResults;

using Kafdoc.Domain.Graph;
using Kafdoc.Domain.Kafka;

namespace Kafdoc.Application.Snapshot;

/// <summary>Default <see cref="IClusterRefreshService"/>: reader → builder → store swap.</summary>
internal sealed class ClusterRefreshService(
    IKafkaClusterReader reader,
    ClusterGraphBuilder builder,
    ISnapshotStore store,
    TimeProvider timeProvider) : IClusterRefreshService
{
    /// <inheritdoc />
    public async Task<Result> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var raw = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var graph = builder.Build(raw);
            store.SetSnapshot(new ClusterSnapshot(graph, timeProvider.GetUtcNow()));
            return Result.Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Refresh must never crash the host; surface every failure as a result.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            store.SetError(ex.Message);
            return Result.Fail(new ExceptionalError(ex));
        }
    }
}
