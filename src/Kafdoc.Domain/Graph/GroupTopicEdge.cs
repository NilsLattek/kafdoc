namespace Kafdoc.Domain.Graph;

/// <summary>A consumer group actually consumes a topic (from committed offsets).</summary>
/// <param name="GroupId">The consumer group id.</param>
/// <param name="Topic">The consumed topic name.</param>
public sealed record GroupTopicEdge(string GroupId, string Topic);
