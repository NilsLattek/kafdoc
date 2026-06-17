using Bunit;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using Kafdoc.Application.Dtos;
using Kafdoc.Application.Services;
using Kafdoc.Web.Components.Pages;

namespace Kafdoc.WebTest;

public sealed class UsersPageTests : Bunit.BunitContext
{
    [Fact]
    public void Users_renders_a_link_to_the_user_detail_page_per_principal()
    {
        // Arrange
        var userQuery = Substitute.For<IUserQueryService>();
        userQuery.GetUsers().Returns(
        [
            new UserSummaryDto("User:alice", HasScramCredentials: true, ProducesCount: 2, ConsumesCount: 1, HasDocumentation: true),
        ]);
        var status = Substitute.For<ISnapshotStatusService>();
        status.GetStatus().Returns(new SnapshotStatusDto(IsReady: true, LastRefresh: DateTimeOffset.UnixEpoch, LastError: null));
        Services.AddSingleton(userQuery);
        Services.AddSingleton(status);

        // Act
        var cut = Render<Users>();

        // Assert
        Assert.Contains("href=\"/users/User%3Aalice\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Documented", cut.Markup, StringComparison.Ordinal);
    }
}
