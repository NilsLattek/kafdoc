namespace Kafdoc.Domain.Kafka;

/// <summary>The complete set of raw facts read from a Kafka cluster in one pass.</summary>
/// <param name="Topics">All topics and their partition counts.</param>
/// <param name="Acls">All ACL bindings.</param>
/// <param name="ConsumerGroups">All consumer groups with their consumed topics.</param>
/// <param name="ScramUsers">All users that have SCRAM credentials.</param>
public sealed record RawClusterData(
    IReadOnlyList<RawTopic> Topics,
    IReadOnlyList<RawAcl> Acls,
    IReadOnlyList<RawConsumerGroup> ConsumerGroups,
    IReadOnlyList<RawScramUser> ScramUsers);
