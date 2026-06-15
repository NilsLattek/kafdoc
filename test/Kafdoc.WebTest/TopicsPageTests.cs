using Bunit;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using Kafdoc.Application.Dtos;
using Kafdoc.Application.Services;
using Kafdoc.Web.Components.Pages;

namespace Kafdoc.WebTest;

public sealed class TopicsPageTests : Bunit.BunitContext
{
    [Fact]
    public void Topics_renders_loading_message_when_snapshot_not_ready()
    {
        // Arrange
        var topicQuery = Substitute.For<ITopicQueryService>();
        var status = Substitute.For<ISnapshotStatusService>();
        status.GetStatus().Returns(new SnapshotStatusDto(IsReady: false, LastRefresh: null, LastError: null));
        Services.AddSingleton(topicQuery);
        Services.AddSingleton(status);

        // Act
        var cut = Render<Topics>();

        // Assert
        Assert.Contains("Loading cluster data", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Topics_renders_a_row_per_topic_when_ready()
    {
        // Arrange
        var topicQuery = Substitute.For<ITopicQueryService>();
        topicQuery.GetTopics().Returns(
        [
            new TopicSummaryDto("orders", 3, 1, 2, HasDocumentation: true),
            new TopicSummaryDto("billing", 1, 0, 1, HasDocumentation: false),
        ]);
        var status = Substitute.For<ISnapshotStatusService>();
        status.GetStatus().Returns(new SnapshotStatusDto(IsReady: true, LastRefresh: DateTimeOffset.UnixEpoch, LastError: null));
        Services.AddSingleton(topicQuery);
        Services.AddSingleton(status);

        // Act
        var cut = Render<Topics>();

        // Assert
        Assert.Contains("orders", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("billing", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("✓", cut.Markup, StringComparison.Ordinal);
    }
}
