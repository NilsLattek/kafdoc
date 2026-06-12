using Kafdoc.Application.Dtos;

namespace Kafdoc.Application.Services;

/// <summary>Exposes snapshot status to the UI.</summary>
public interface ISnapshotStatusService
{
    /// <summary>Returns the current snapshot status.</summary>
    SnapshotStatusDto GetStatus();
}
