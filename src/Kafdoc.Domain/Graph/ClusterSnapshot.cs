namespace Kafdoc.Domain.Graph;

/// <summary>A cluster graph captured at a point in time.</summary>
/// <param name="Graph">The producer/consumer graph.</param>
/// <param name="CapturedAt">When the graph was captured.</param>
public sealed record ClusterSnapshot(ClusterGraph Graph, DateTimeOffset CapturedAt);
