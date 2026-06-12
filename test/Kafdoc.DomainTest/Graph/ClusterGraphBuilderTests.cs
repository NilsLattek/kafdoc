using Kafdoc.Domain.Graph;
using Kafdoc.Domain.Kafka;

namespace Kafdoc.DomainTest.Graph;

public class ClusterGraphBuilderTests
{
    private static RawClusterData Raw(
        IReadOnlyList<RawTopic>? topics = null,
        IReadOnlyList<RawAcl>? acls = null,
        IReadOnlyList<RawConsumerGroup>? groups = null,
        IReadOnlyList<RawScramUser>? scram = null) =>
        new(topics ?? [], acls ?? [], groups ?? [], scram ?? []);

    private static RawAcl Acl(
        string principal,
        KafkaResourceType type,
        string name,
        KafkaAclOperation op,
        KafkaResourcePatternType pattern = KafkaResourcePatternType.Literal,
        KafkaAclPermission permission = KafkaAclPermission.Allow) =>
        new(principal, type, name, pattern, op, permission);

    [Fact]
    public void Build_maps_write_acl_to_producer_edge()
    {
        // Arrange
        var builder = new ClusterGraphBuilder();
        var raw = Raw(
            topics: [new RawTopic("orders", 3)],
            acls: [Acl("User:svc-orders", KafkaResourceType.Topic, "orders", KafkaAclOperation.Write)]);

        // Act
        var graph = builder.Build(raw);

        // Assert
        Assert.Contains(new ProducerEdge("User:svc-orders", "orders"), graph.Producers);
        Assert.Empty(graph.Consumers);
    }

    [Fact]
    public void Build_maps_read_acl_on_topic_to_consumer_edge()
    {
        // Arrange
        var builder = new ClusterGraphBuilder();
        var raw = Raw(
            topics: [new RawTopic("orders", 3)],
            acls: [Acl("User:svc-billing", KafkaResourceType.Topic, "orders", KafkaAclOperation.Read)]);

        // Act
        var graph = builder.Build(raw);

        // Assert
        Assert.Contains(new ConsumerEdge("User:svc-billing", "orders"), graph.Consumers);
        Assert.Empty(graph.Producers);
    }

    [Fact]
    public void Build_maps_read_acl_on_group_to_user_group_edge()
    {
        // Arrange
        var builder = new ClusterGraphBuilder();
        var raw = Raw(
            groups: [new RawConsumerGroup("billing-app", "Stable", 2, [])],
            acls: [Acl("User:svc-billing", KafkaResourceType.Group, "billing-app", KafkaAclOperation.Read)]);

        // Act
        var graph = builder.Build(raw);

        // Assert
        Assert.Contains(new UserGroupEdge("User:svc-billing", "billing-app"), graph.UserGroups);
    }

    [Fact]
    public void Build_maps_group_committed_offsets_to_group_topic_edges()
    {
        // Arrange
        var builder = new ClusterGraphBuilder();
        var raw = Raw(
            topics: [new RawTopic("orders", 3)],
            groups: [new RawConsumerGroup("billing-app", "Stable", 2, ["orders"])]);

        // Act
        var graph = builder.Build(raw);

        // Assert
        Assert.Contains(new GroupTopicEdge("billing-app", "orders"), graph.GroupConsumption);
    }

    [Fact]
    public void Build_expands_prefixed_topic_acl_over_matching_topics()
    {
        // Arrange
        var builder = new ClusterGraphBuilder();
        var raw = Raw(
            topics: [new RawTopic("orders.created", 1), new RawTopic("orders.shipped", 1), new RawTopic("billing.paid", 1)],
            acls: [Acl("User:svc-orders", KafkaResourceType.Topic, "orders.", KafkaAclOperation.Write, KafkaResourcePatternType.Prefixed)]);

        // Act
        var graph = builder.Build(raw);

        // Assert
        Assert.Contains(new ProducerEdge("User:svc-orders", "orders.created"), graph.Producers);
        Assert.Contains(new ProducerEdge("User:svc-orders", "orders.shipped"), graph.Producers);
        Assert.DoesNotContain(new ProducerEdge("User:svc-orders", "billing.paid"), graph.Producers);
    }

    [Fact]
    public void Build_treats_wildcard_literal_acl_as_all_topics()
    {
        // Arrange
        var builder = new ClusterGraphBuilder();
        var raw = Raw(
            topics: [new RawTopic("a", 1), new RawTopic("b", 1)],
            acls: [Acl("User:admin", KafkaResourceType.Topic, "*", KafkaAclOperation.Read)]);

        // Act
        var graph = builder.Build(raw);

        // Assert
        Assert.Contains(new ConsumerEdge("User:admin", "a"), graph.Consumers);
        Assert.Contains(new ConsumerEdge("User:admin", "b"), graph.Consumers);
    }

    [Fact]
    public void Build_includes_principals_without_scram_as_users()
    {
        // Arrange
        var builder = new ClusterGraphBuilder();
        var raw = Raw(
            topics: [new RawTopic("orders", 1)],
            acls: [Acl("User:mtls-client", KafkaResourceType.Topic, "orders", KafkaAclOperation.Read)],
            scram: [new RawScramUser("User:svc-billing")]);

        // Act
        var graph = builder.Build(raw);

        // Assert
        Assert.Contains(graph.Users, u => string.Equals(u.Principal, "User:mtls-client", StringComparison.Ordinal) && !u.HasScramCredentials);
        Assert.Contains(graph.Users, u => string.Equals(u.Principal, "User:svc-billing", StringComparison.Ordinal) && u.HasScramCredentials);
    }

    [Fact]
    public void Build_ignores_deny_acls_for_edges()
    {
        // Arrange
        var builder = new ClusterGraphBuilder();
        var raw = Raw(
            topics: [new RawTopic("orders", 1)],
            acls: [Acl("User:blocked", KafkaResourceType.Topic, "orders", KafkaAclOperation.Write, permission: KafkaAclPermission.Deny)]);

        // Act
        var graph = builder.Build(raw);

        // Assert
        Assert.Empty(graph.Producers);
        Assert.Contains(graph.Users, u => string.Equals(u.Principal, "User:blocked", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_deduplicates_overlapping_edges()
    {
        // Arrange — an All ACL and a Write ACL both imply produce
        var builder = new ClusterGraphBuilder();
        var raw = Raw(
            topics: [new RawTopic("orders", 1)],
            acls:
            [
                Acl("User:svc", KafkaResourceType.Topic, "orders", KafkaAclOperation.Write),
                Acl("User:svc", KafkaResourceType.Topic, "orders", KafkaAclOperation.All),
            ]);

        // Act
        var graph = builder.Build(raw);

        // Assert
        Assert.Single(graph.Producers, e => e == new ProducerEdge("User:svc", "orders"));
        Assert.Contains(new ConsumerEdge("User:svc", "orders"), graph.Consumers);
    }
}
