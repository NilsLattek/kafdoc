using Kafdoc.Domain.Graph;
using Kafdoc.Domain.Kafka;

namespace Kafdoc.DomainTest.Kafka;

public class RawClusterDataFilterTests
{
    private static RawClusterData Raw(
        IReadOnlyList<RawTopic>? topics = null,
        IReadOnlyList<RawAcl>? acls = null,
        IReadOnlyList<RawConsumerGroup>? groups = null,
        IReadOnlyList<RawScramUser>? scram = null) =>
        new(topics ?? [], acls ?? [], groups ?? [], scram ?? []);

    private static RawAcl Acl(string principal, string resource) =>
        new(principal, KafkaResourceType.Topic, resource,
            KafkaResourcePatternType.Literal, KafkaAclOperation.Write, KafkaAclPermission.Allow);

    [Fact]
    public void Apply_keeps_topics_matching_a_prefix()
    {
        // Arrange
        var filter = new RawClusterDataFilter(new ClusterFilterOptions { TopicPrefixes = ["qa."] });
        var raw = Raw(topics: [new RawTopic("qa.orders", 1), new RawTopic("dev.orders", 1)]);

        // Act
        var result = filter.Apply(raw);

        // Assert
        Assert.Contains(result.Topics, t => string.Equals(t.Name, "qa.orders", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Topics, t => string.Equals(t.Name, "dev.orders", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_with_empty_topic_prefixes_keeps_all_topics()
    {
        // Arrange
        var filter = new RawClusterDataFilter(new ClusterFilterOptions());
        var raw = Raw(topics: [new RawTopic("qa.orders", 1), new RawTopic("dev.orders", 1)]);

        // Act
        var result = filter.Apply(raw);

        // Assert
        Assert.Equal(2, result.Topics.Count);
    }

    [Fact]
    public void Apply_keeps_users_matching_prefix_after_stripping_user_type()
    {
        // Arrange
        var filter = new RawClusterDataFilter(new ClusterFilterOptions { UserPrefixes = ["qa-"] });
        var raw = Raw(scram: [new RawScramUser("User:qa-svc"), new RawScramUser("User:dev-svc")]);

        // Act
        var result = filter.Apply(raw);

        // Assert
        Assert.Contains(result.ScramUsers, u => string.Equals(u.Principal, "User:qa-svc", StringComparison.Ordinal));
        Assert.DoesNotContain(result.ScramUsers, u => string.Equals(u.Principal, "User:dev-svc", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_keeps_acls_whose_principal_matches_user_prefix()
    {
        // Arrange
        var filter = new RawClusterDataFilter(new ClusterFilterOptions { UserPrefixes = ["qa-"] });
        var raw = Raw(acls: [Acl("User:qa-svc", "qa.orders"), Acl("User:dev-svc", "dev.orders")]);

        // Act
        var result = filter.Apply(raw);

        // Assert
        Assert.Contains(result.Acls, a => string.Equals(a.Principal, "User:qa-svc", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Acls, a => string.Equals(a.Principal, "User:dev-svc", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_keeps_groups_matching_prefix_and_projects_consumed_topics()
    {
        // Arrange
        var filter = new RawClusterDataFilter(new ClusterFilterOptions
        {
            TopicPrefixes = ["qa."],
            GroupPrefixes = ["qa."],
        });
        var raw = Raw(groups:
        [
            new RawConsumerGroup("qa.readers", "Stable", 1, ["qa.orders", "dev.orders"]),
            new RawConsumerGroup("dev.readers", "Stable", 1, ["dev.orders"]),
        ]);

        // Act
        var result = filter.Apply(raw);

        // Assert
        var group = Assert.Single(result.ConsumerGroups);
        Assert.Equal("qa.readers", group.GroupId);
        Assert.Equal(["qa.orders"], group.ConsumedTopics);
    }

    [Fact]
    public void Apply_with_all_prefix_lists_empty_returns_equivalent_data()
    {
        // Arrange
        var filter = new RawClusterDataFilter(new ClusterFilterOptions());
        var raw = Raw(
            topics: [new RawTopic("qa.orders", 1)],
            acls: [Acl("User:qa-svc", "qa.orders")],
            groups: [new RawConsumerGroup("qa.readers", "Stable", 1, ["qa.orders"])],
            scram: [new RawScramUser("User:qa-svc")]);

        // Act
        var result = filter.Apply(raw);

        // Assert
        Assert.Single(result.Topics);
        Assert.Single(result.Acls);
        Assert.Single(result.ConsumerGroups);
        Assert.Single(result.ScramUsers);
    }

    [Fact]
    public void Apply_throws_when_raw_is_null()
    {
        // Arrange
        var filter = new RawClusterDataFilter(new ClusterFilterOptions());

        // Act + Assert
        Assert.Throws<ArgumentNullException>((Action)(() => filter.Apply(null!)));
    }

    [Fact]
    public void Filtered_acl_referencing_a_dropped_topic_yields_no_edge()
    {
        // Arrange
        var filter = new RawClusterDataFilter(new ClusterFilterOptions
        {
            TopicPrefixes = ["qa."],
            UserPrefixes = ["qa-"],
        });
        var raw = Raw(
            topics: [new RawTopic("qa.orders", 1), new RawTopic("dev.orders", 1)],
            acls:
            [
                Acl("User:qa-svc", "qa.orders"),
                Acl("User:qa-svc", "dev.orders"),
            ]);

        // Act
        var graph = new ClusterGraphBuilder().Build(filter.Apply(raw));

        // Assert
        Assert.Contains(new ProducerEdge("User:qa-svc", "qa.orders"), graph.Producers);
        Assert.DoesNotContain(new ProducerEdge("User:qa-svc", "dev.orders"), graph.Producers);
    }
}
