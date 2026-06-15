using Kafdoc.Application.Services;
using Kafdoc.Application.Snapshot;
using Kafdoc.Domain.Documentation;
using Kafdoc.Domain.Graph;

using NSubstitute;

namespace Kafdoc.ApplicationTest.Services;

public class UserQueryServiceTests
{
    private static SnapshotStore StoreWith(ClusterGraph graph)
    {
        var store = new SnapshotStore();
        store.SetSnapshot(new ClusterSnapshot(graph, DateTimeOffset.UnixEpoch));
        return store;
    }

    private static IDocumentationStore NoDocs()
    {
        var docs = Substitute.For<IDocumentationStore>();
        docs.ListSlugs(Arg.Any<DocumentationKind>()).Returns(new HashSet<string>(StringComparer.Ordinal));
        docs.Read(Arg.Any<DocumentationKind>(), Arg.Any<string>())
            .Returns(ci => new DocumentationLookup($"users/{ci.ArgAt<string>(1)}.md", null));
        return docs;
    }

    [Fact]
    public void GetUsers_returns_empty_when_no_snapshot()
    {
        // Arrange
        var service = new UserQueryService(new SnapshotStore(), NoDocs());

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
        var service = new UserQueryService(StoreWith(graph), NoDocs());

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
            new ClusterGraph([], [], [], [], [], [], [])), NoDocs());

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
        var service = new UserQueryService(StoreWith(graph), NoDocs());

        // Act
        var detail = service.GetUser("User:c");

        // Assert
        Assert.NotNull(detail);
        Assert.Empty(detail!.ProducesTopics);
        Assert.Equal(["orders"], detail.ConsumesTopics);
        Assert.Equal(["g1"], detail.Groups);
    }

    [Fact]
    public void GetUsers_sets_HasDocumentation_from_the_doc_store()
    {
        // Arrange
        var graph = new ClusterGraph(
            Topics: [],
            Users: [new KafkaUser("User:documented", false), new KafkaUser("User:bare", false)],
            ConsumerGroups: [], Producers: [], Consumers: [], UserGroups: [], GroupConsumption: []);
        var docs = Substitute.For<IDocumentationStore>();
        docs.ListSlugs(DocumentationKind.User).Returns(new HashSet<string>(StringComparer.Ordinal) { "documented" });
        var service = new UserQueryService(StoreWith(graph), docs);

        // Act
        var users = service.GetUsers();

        // Assert — ordered by principal: "User:bare" then "User:documented"
        Assert.False(users[0].HasDocumentation);
        Assert.True(users[1].HasDocumentation);
    }

    [Fact]
    public void GetUser_includes_documentation_content_and_path()
    {
        // Arrange
        var graph = new ClusterGraph(
            Topics: [], Users: [new KafkaUser("User:c", false)],
            ConsumerGroups: [], Producers: [], Consumers: [], UserGroups: [], GroupConsumption: []);
        var docs = Substitute.For<IDocumentationStore>();
        docs.ListSlugs(Arg.Any<DocumentationKind>()).Returns(new HashSet<string>(StringComparer.Ordinal));
        docs.Read(DocumentationKind.User, "User:c").Returns(new DocumentationLookup("users/c.md", "# hello"));
        var service = new UserQueryService(StoreWith(graph), docs);

        // Act
        var detail = service.GetUser("User:c");

        // Assert
        Assert.NotNull(detail);
        Assert.Equal("users/c.md", detail!.DocumentationPath);
        Assert.Equal("# hello", detail.Documentation);
    }
}
