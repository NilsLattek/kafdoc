using Kafdoc.Application.Services;
using Kafdoc.Application.Snapshot;
using Kafdoc.Domain.Graph;

namespace Kafdoc.ApplicationTest.Services;

public class TopicQueryServiceTests
{
    private static SnapshotStore StoreWith(ClusterGraph graph)
    {
        var store = new SnapshotStore();
        store.SetSnapshot(new ClusterSnapshot(graph, DateTimeOffset.UnixEpoch));
        return store;
    }

    [Fact]
    public void GetTopics_returns_empty_when_no_snapshot()
    {
        // Arrange
        var service = new TopicQueryService(new SnapshotStore());

        // Act
        var topics = service.GetTopics();

        // Assert
        Assert.Empty(topics);
    }

    [Fact]
    public void GetTopics_counts_producers_and_consumer_groups()
    {
        // Arrange
        var graph = new ClusterGraph(
            Topics: [new KafkaTopic("orders", 3)],
            Users: [new KafkaUser("User:p", false), new KafkaUser("User:q", false)],
            ConsumerGroups: [new KafkaConsumerGroup("g1", "Stable", 1)],
            Producers: [new ProducerEdge("User:p", "orders"), new ProducerEdge("User:q", "orders")],
            Consumers: [],
            UserGroups: [],
            GroupConsumption: [new GroupTopicEdge("g1", "orders")]);
        var service = new TopicQueryService(StoreWith(graph));

        // Act
        var topic = Assert.Single(service.GetTopics());

        // Assert
        Assert.Equal("orders", topic.Name);
        Assert.Equal(2, topic.ProducerCount);
        Assert.Equal(1, topic.ConsumerGroupCount);
    }

    [Fact]
    public void GetTopic_returns_null_for_unknown_topic()
    {
        // Arrange
        var service = new TopicQueryService(StoreWith(
            new ClusterGraph([], [], [], [], [], [], [])));

        // Act + Assert
        Assert.Null(service.GetTopic("missing"));
    }

    [Fact]
    public void GetTopic_includes_producers_groups_and_read_only_principals()
    {
        // Arrange — User:r can read the topic but backs no consuming group
        var graph = new ClusterGraph(
            Topics: [new KafkaTopic("orders", 1)],
            Users: [new KafkaUser("User:p", false), new KafkaUser("User:c", false), new KafkaUser("User:r", false)],
            ConsumerGroups: [new KafkaConsumerGroup("g1", "Stable", 1)],
            Producers: [new ProducerEdge("User:p", "orders")],
            Consumers: [new ConsumerEdge("User:c", "orders"), new ConsumerEdge("User:r", "orders")],
            UserGroups: [new UserGroupEdge("User:c", "g1")],
            GroupConsumption: [new GroupTopicEdge("g1", "orders")]);
        var service = new TopicQueryService(StoreWith(graph));

        // Act
        var detail = service.GetTopic("orders");

        // Assert
        Assert.NotNull(detail);
        Assert.Equal(["User:p"], detail!.Producers);
        var group = Assert.Single(detail.ConsumerGroups);
        Assert.Equal("g1", group.GroupId);
        Assert.Equal(["User:c"], group.Principals);
        Assert.Equal(["User:r"], detail.ReadOnlyPrincipals);
    }
}
