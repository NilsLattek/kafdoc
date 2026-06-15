using Bunit;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using Kafdoc.Application.Dtos;
using Kafdoc.Application.Services;
using Kafdoc.Web.Components.Pages;

namespace Kafdoc.WebTest;

public sealed class UserDetailPageTests : Bunit.BunitContext
{
    [Fact]
    public void UserDetail_renders_produce_and_consume_topic_links_for_a_known_principal()
    {
        // Arrange
        var userQuery = Substitute.For<IUserQueryService>();
        userQuery.GetUser("User:alice").Returns(new UserDetailDto(
            Principal: "User:alice",
            HasScramCredentials: true,
            ProducesTopics: ["orders", "payments"],
            ConsumesTopics: ["shipments"],
            Groups: ["billing-svc"]));
        Services.AddSingleton(userQuery);

        // Act
        var cut = Render<UserDetail>(ps => ps.Add(p => p.Principal, "User:alice"));

        // Assert
        Assert.Contains("href=\"/topics/orders\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("href=\"/topics/payments\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("href=\"/topics/shipments\"", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void UserDetail_renders_not_found_for_an_unknown_principal()
    {
        // Arrange
        var userQuery = Substitute.For<IUserQueryService>();
        userQuery.GetUser(Arg.Any<string>()).Returns((UserDetailDto?)null);
        Services.AddSingleton(userQuery);

        // Act
        var cut = Render<UserDetail>(ps => ps.Add(p => p.Principal, "User:ghost"));

        // Assert
        Assert.Contains("User not found", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void UserDetail_renders_empty_note_when_user_has_no_produce_topics()
    {
        // Arrange
        var userQuery = Substitute.For<IUserQueryService>();
        userQuery.GetUser("User:reader").Returns(new UserDetailDto(
            Principal: "User:reader",
            HasScramCredentials: false,
            ProducesTopics: [],
            ConsumesTopics: ["shipments"],
            Groups: []));
        Services.AddSingleton(userQuery);

        // Act
        var cut = Render<UserDetail>(ps => ps.Add(p => p.Principal, "User:reader"));

        // Assert
        Assert.Contains("No producer ACLs", cut.Markup, StringComparison.Ordinal);
    }
}
