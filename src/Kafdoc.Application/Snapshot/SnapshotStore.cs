using Kafdoc.Domain.Graph;

namespace Kafdoc.Application.Snapshot;

/// <summary>Thread-safe singleton implementation of <see cref="ISnapshotStore"/>.</summary>
internal sealed class SnapshotStore : ISnapshotStore
{
    private volatile ClusterSnapshot? _current;
    private volatile string? _lastError;

    /// <inheritdoc />
    public ClusterSnapshot? Current => _current;

    /// <inheritdoc />
    public DateTimeOffset? LastRefresh => _current?.CapturedAt;

    /// <inheritdoc />
    public string? LastError => _lastError;

    /// <inheritdoc />
    public bool IsReady => _current is not null;

    /// <inheritdoc />
    public void SetSnapshot(ClusterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _current = snapshot;
        _lastError = null;
    }

    /// <inheritdoc />
    public void SetError(string error) => _lastError = error;
}
