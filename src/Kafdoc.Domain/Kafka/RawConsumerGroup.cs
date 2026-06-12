namespace Kafdoc.Domain.Kafka;

/// <summary>A consumer group as read from the cluster.</summary>
/// <param name="GroupId">The group id.</param>
/// <param name="State">The group state, e.g. <c>Stable</c> or <c>Empty</c>.</param>
/// <param name="MemberCount">The number of active members.</param>
/// <param name="ConsumedTopics">The distinct topics the group has committed offsets for.</param>
public sealed record RawConsumerGroup(
    string GroupId,
    string State,
    int MemberCount,
    IReadOnlyList<string> ConsumedTopics);
