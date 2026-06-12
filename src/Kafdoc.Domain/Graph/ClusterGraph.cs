namespace Kafdoc.Domain.Graph;

/// <summary>
/// The assembled producer/consumer graph: typed nodes and the edges derived from
/// ACLs and consumer-group metadata.
/// </summary>
/// <param name="Topics">Topic nodes.</param>
/// <param name="Users">User (principal) nodes.</param>
/// <param name="ConsumerGroups">Consumer group nodes.</param>
/// <param name="Producers">User → topic produce edges (from WRITE ACLs).</param>
/// <param name="Consumers">User → topic consume edges (from READ ACLs on topics).</param>
/// <param name="UserGroups">User → group edges (from READ ACLs on group resources).</param>
/// <param name="GroupConsumption">Group → topic edges (from committed offsets).</param>
public sealed record ClusterGraph(
    IReadOnlyList<KafkaTopic> Topics,
    IReadOnlyList<KafkaUser> Users,
    IReadOnlyList<KafkaConsumerGroup> ConsumerGroups,
    IReadOnlyList<ProducerEdge> Producers,
    IReadOnlyList<ConsumerEdge> Consumers,
    IReadOnlyList<UserGroupEdge> UserGroups,
    IReadOnlyList<GroupTopicEdge> GroupConsumption);
