using Kafdoc.Domain.Graph;

namespace Kafdoc.Application.Snapshot;

/// <summary>
/// Holds the current in-memory cluster snapshot and the status of the last refresh.
/// Implementations are thread-safe singletons; reads are lock-free.
/// </summary>
public interface ISnapshotStore
{
    /// <summary>The most recently captured snapshot, or <see langword="null"/> before the first refresh.</summary>
    ClusterSnapshot? Current { get; }

    /// <summary>When the last successful refresh completed, or <see langword="null"/> if none yet.</summary>
    DateTimeOffset? LastRefresh { get; }

    /// <summary>The error message from the last failed refresh, or <see langword="null"/> if the last refresh succeeded.</summary>
    string? LastError { get; }

    /// <summary>Whether at least one snapshot has been captured.</summary>
    bool IsReady { get; }

    /// <summary>Atomically replaces the current snapshot and clears the last error.</summary>
    /// <param name="snapshot">The new snapshot.</param>
    void SetSnapshot(ClusterSnapshot snapshot);

    /// <summary>Records that the last refresh failed, leaving the existing snapshot in place.</summary>
    /// <param name="error">The error message.</param>
#pragma warning disable CA1716 // "error" is the clearest name here; the cross-language keyword clash is acceptable.
    void SetError(string error);
#pragma warning restore CA1716
}
