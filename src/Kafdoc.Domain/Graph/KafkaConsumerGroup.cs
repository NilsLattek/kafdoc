namespace Kafdoc.Domain.Graph;

/// <summary>A consumer group node in the cluster graph.</summary>
/// <param name="GroupId">The group id.</param>
/// <param name="State">The group state, e.g. <c>Stable</c> or <c>Empty</c>.</param>
/// <param name="MemberCount">The number of active members.</param>
public sealed record KafkaConsumerGroup(string GroupId, string State, int MemberCount);
