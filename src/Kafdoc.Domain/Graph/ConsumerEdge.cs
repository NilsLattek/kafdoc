namespace Kafdoc.Domain.Graph;

/// <summary>A user is permitted to consume a topic (from a <c>READ</c> ACL on the topic).</summary>
/// <param name="Principal">The consuming principal.</param>
/// <param name="Topic">The target topic name.</param>
public sealed record ConsumerEdge(string Principal, string Topic);
