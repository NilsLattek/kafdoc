using Bunit;

using Markdig;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using Kafdoc.Application.Dtos;
using Kafdoc.Application.Services;
using Kafdoc.Web.Components.Pages;

namespace Kafdoc.WebTest;

public sealed class UserDetailPageTests : Bunit.BunitContext
{
    private void RegisterPipeline()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build());
    }

    [Fact]
    public void UserDetail_renders_produce_and_consume_topic_links_for_a_known_principal()
    {
        // Arrange
        RegisterPipeline();
        var userQuery = Substitute.For<IUserQueryService>();
        userQuery.GetUser("User:alice").Returns(new UserDetailDto(
            Principal: "User:alice",
            HasScramCredentials: true,
            ProducesTopics: ["orders", "payments"],
            ConsumesTopics: ["shipments"],
            Groups: ["billing-svc"],
            DocumentationPath: "users/alice.md",
            Documentation: "# Alice service"));
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
        RegisterPipeline();
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
        RegisterPipeline();
        var userQuery = Substitute.For<IUserQueryService>();
        userQuery.GetUser("User:reader").Returns(new UserDetailDto(
            Principal: "User:reader",
            HasScramCredentials: false,
            ProducesTopics: [],
            ConsumesTopics: ["shipments"],
            Groups: [],
            DocumentationPath: "users/reader.md",
            Documentation: null));
        Services.AddSingleton(userQuery);

        // Act
        var cut = Render<UserDetail>(ps => ps.Add(p => p.Principal, "User:reader"));

        // Assert
        Assert.Contains("No producer ACLs", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void UserDetail_renders_documentation_markdown_when_present()
    {
        // Arrange
        RegisterPipeline();
        var userQuery = Substitute.For<IUserQueryService>();
        userQuery.GetUser("User:alice").Returns(new UserDetailDto(
            Principal: "User:alice",
            HasScramCredentials: true,
            ProducesTopics: [],
            ConsumesTopics: [],
            Groups: [],
            DocumentationPath: "users/alice.md",
            Documentation: "# Alice service"));
        Services.AddSingleton(userQuery);

        // Act
        var cut = Render<UserDetail>(ps => ps.Add(p => p.Principal, "User:alice"));

        // Assert — the markdown "# Alice service" renders as its own <h1>, distinct from
        // the page's principal heading (which contains "User:alice", not "Alice service")
        Assert.Contains("Alice service</h1>", cut.Markup, StringComparison.Ordinal);
    }
}
