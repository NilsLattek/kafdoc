using Bunit;

using Markdig;

using Microsoft.Extensions.DependencyInjection;

using Kafdoc.Web.Components.Shared;

namespace Kafdoc.WebTest;

public sealed class MarkdownContentTests : Bunit.BunitContext
{
    private void RegisterPipeline() =>
        Services.AddSingleton(new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build());

    [Fact]
    public void Renders_markdown_as_html_with_a_source_caption()
    {
        // Arrange
        RegisterPipeline();

        // Act
        var cut = Render<MarkdownContent>(ps => ps
            .Add(p => p.Markdown, "# Title")
            .Add(p => p.Path, "topics/orders.md"));

        // Assert
        Assert.Contains("<h1", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Source:", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("topics/orders.md", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Shows_filename_hint_when_no_markdown_is_present()
    {
        // Arrange
        RegisterPipeline();

        // Act
        var cut = Render<MarkdownContent>(ps => ps
            .Add(p => p.Markdown, (string?)null)
            .Add(p => p.Path, "users/svc-payments.md"));

        // Assert
        Assert.Contains("No additional information available", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("users/svc-payments.md", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Escapes_raw_html_in_markdown()
    {
        // Arrange
        RegisterPipeline();

        // Act
        var cut = Render<MarkdownContent>(ps => ps
            .Add(p => p.Markdown, "<script>alert(1)</script>")
            .Add(p => p.Path, "topics/x.md"));

        // Assert — DisableHtml escapes the tag instead of emitting it
        Assert.DoesNotContain("<script>", cut.Markup, StringComparison.Ordinal);
    }
}
