using Kafdoc.Application.Dtos;
using Kafdoc.Application.Snapshot;

namespace Kafdoc.Application.Services;

/// <summary>Exposes the snapshot store status as a DTO.</summary>
internal sealed class SnapshotStatusService(ISnapshotStore store) : ISnapshotStatusService
{
    /// <inheritdoc />
    public SnapshotStatusDto GetStatus() =>
        new(store.IsReady, store.LastRefresh, store.LastError);
}
