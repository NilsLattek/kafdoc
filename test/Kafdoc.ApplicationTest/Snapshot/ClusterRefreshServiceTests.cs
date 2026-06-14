using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using Kafdoc.Application.Snapshot;
using Kafdoc.Domain.Graph;
using Kafdoc.Domain.Kafka;

namespace Kafdoc.ApplicationTest.Snapshot;

public class ClusterRefreshServiceTests
{
    private static RawClusterData EmptyRaw() => new([], [], [], []);

    [Fact]
    public async Task RefreshAsync_stores_snapshot_stamped_with_current_time()
    {
        // Arrange
        var reader = Substitute.For<IKafkaClusterReader>();
        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(EmptyRaw());
        var store = new SnapshotStore();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 12, 8, 0, 0, TimeSpan.Zero));
        var service = new ClusterRefreshService(
            reader, new RawClusterDataFilter(new ClusterFilterOptions()), new ClusterGraphBuilder(), store, time);

        // Act
        var result = await service.RefreshAsync(CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(store.Current);
        Assert.Equal(time.GetUtcNow(), store.Current!.CapturedAt);
    }

    [Fact]
    public async Task RefreshAsync_keeps_previous_snapshot_when_read_fails()
    {
        // Arrange
        var reader = Substitute.For<IKafkaClusterReader>();
        var store = new SnapshotStore();
        var goodSnapshot = new ClusterSnapshot(
            new ClusterGraph([], [], [], [], [], [], []),
            new DateTimeOffset(2026, 6, 12, 7, 0, 0, TimeSpan.Zero));
        store.SetSnapshot(goodSnapshot);
        reader.ReadAsync(Arg.Any<CancellationToken>())
            .Returns<RawClusterData>(_ => throw new InvalidOperationException("broker down"));
        var service = new ClusterRefreshService(
            reader, new RawClusterDataFilter(new ClusterFilterOptions()),
            new ClusterGraphBuilder(), store, new FakeTimeProvider());

        // Act
        var result = await service.RefreshAsync(CancellationToken.None);

        // Assert
        Assert.True(result.IsFailed);
        Assert.Same(goodSnapshot, store.Current);
        Assert.NotNull(store.LastError);
    }

    [Fact]
    public async Task RefreshAsync_applies_filter_before_building()
    {
        // Arrange
        var reader = Substitute.For<IKafkaClusterReader>();
        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(new RawClusterData(
            [new RawTopic("qa.orders", 1), new RawTopic("dev.orders", 1)],
            [], [], []));
        var filter = new RawClusterDataFilter(new ClusterFilterOptions { TopicPrefixes = ["qa."] });
        var store = new SnapshotStore();
        var service = new ClusterRefreshService(
            reader, filter, new ClusterGraphBuilder(), store, new FakeTimeProvider());

        // Act
        var result = await service.RefreshAsync(CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var topicNames = store.Current!.Graph.Topics.Select(t => t.Name).ToList();
        Assert.Contains("qa.orders", topicNames);
        Assert.DoesNotContain("dev.orders", topicNames);
    }
}
