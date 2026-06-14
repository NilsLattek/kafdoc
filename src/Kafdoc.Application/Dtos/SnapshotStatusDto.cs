namespace Kafdoc.Application.Dtos;

/// <summary>The status of the in-memory snapshot for display in the UI.</summary>
/// <param name="IsReady">Whether a snapshot has been captured.</param>
/// <param name="LastRefresh">When the last successful refresh completed.</param>
/// <param name="LastError">The last refresh error, if any.</param>
public sealed record SnapshotStatusDto(bool IsReady, DateTimeOffset? LastRefresh, string? LastError);
