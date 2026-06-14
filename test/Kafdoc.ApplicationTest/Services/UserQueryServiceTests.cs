using Kafdoc.Application.Services;
using Kafdoc.Application.Snapshot;
using Kafdoc.Domain.Graph;

namespace Kafdoc.ApplicationTest.Services;

public class UserQueryServiceTests
{
    private static SnapshotStore StoreWith(ClusterGraph graph)
    {
        var store = new SnapshotStore();
        store.SetSnapshot(new ClusterSnapshot(graph, DateTimeOffset.UnixEpoch));
        return store;
    }

    [Fact]
    public void GetUsers_returns_empty_when_no_snapshot()
    {
        // Arrange
        var service = new UserQueryService(new SnapshotStore());

        // Act
        var users = service.GetUsers();

        // Assert
        Assert.Empty(users);
    }

    [Fact]
    public void GetUsers_counts_distinct_produced_and_consumed_topics_ordered_by_principal()
    {
        // Arrange
        var graph = new ClusterGraph(
            Topics: [new KafkaTopic("orders", 1), new KafkaTopic("shipments", 1)],
            Users: [new KafkaUser("User:p", true), new KafkaUser("User:c", false)],
            ConsumerGroups: [],
            Producers: [new ProducerEdge("User:p", "orders"), new ProducerEdge("User:p", "shipments")],
            Consumers: [new ConsumerEdge("User:c", "orders")],
            UserGroups: [],
            GroupConsumption: []);
        var service = new UserQueryService(StoreWith(graph));

        // Act
        var users = service.GetUsers();

        // Assert — ordered by principal: "User:c" before "User:p"
        Assert.Equal(2, users.Count);
        Assert.Equal("User:c", users[0].Principal);
        Assert.False(users[0].HasScramCredentials);
        Assert.Equal(0, users[0].ProducesCount);
        Assert.Equal(1, users[0].ConsumesCount);
        Assert.Equal("User:p", users[1].Principal);
        Assert.True(users[1].HasScramCredentials);
        Assert.Equal(2, users[1].ProducesCount);
        Assert.Equal(0, users[1].ConsumesCount);
    }

    [Fact]
    public void GetUser_returns_null_for_unknown_principal()
    {
        // Arrange
        var service = new UserQueryService(StoreWith(
            new ClusterGraph([], [], [], [], [], [], [])));

        // Act + Assert
        Assert.Null(service.GetUser("User:missing"));
    }

    [Fact]
    public void GetUser_includes_produced_consumed_topics_and_groups()
    {
        // Arrange
        var graph = new ClusterGraph(
            Topics: [new KafkaTopic("orders", 1)],
            Users: [new KafkaUser("User:c", false)],
            ConsumerGroups: [new KafkaConsumerGroup("g1", "Stable", 1)],
            Producers: [],
            Consumers: [new ConsumerEdge("User:c", "orders")],
            UserGroups: [new UserGroupEdge("User:c", "g1")],
            GroupConsumption: [new GroupTopicEdge("g1", "orders")]);
        var service = new UserQueryService(StoreWith(graph));

        // Act
        var detail = service.GetUser("User:c");

        // Assert
        Assert.NotNull(detail);
        Assert.Empty(detail!.ProducesTopics);
        Assert.Equal(["orders"], detail.ConsumesTopics);
        Assert.Equal(["g1"], detail.Groups);
    }
}
