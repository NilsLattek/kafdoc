namespace Kafdoc.Domain.Graph;

/// <summary>A user is permitted to produce to a topic (from a <c>WRITE</c> ACL).</summary>
/// <param name="Principal">The producing principal.</param>
/// <param name="Topic">The target topic name.</param>
public sealed record ProducerEdge(string Principal, string Topic);
