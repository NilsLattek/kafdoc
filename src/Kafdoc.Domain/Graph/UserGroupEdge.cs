namespace Kafdoc.Domain.Graph;

/// <summary>A user backs a consumer group (from a <c>READ</c> ACL on the group resource).</summary>
/// <param name="Principal">The principal that owns/uses the group.</param>
/// <param name="GroupId">The consumer group id.</param>
public sealed record UserGroupEdge(string Principal, string GroupId);
