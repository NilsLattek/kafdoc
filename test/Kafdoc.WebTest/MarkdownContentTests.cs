using Bunit;

using Markdig;

using Microsoft.Extensions.DependencyInjection;

using Kafdoc.Web.Components.Shared;

namespace Kafdoc.WebTest;

public sealed class MarkdownContentTests : Bunit.BunitContext
{
    private void RegisterPipeline()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new MarkdownPipelineBuilder().UseAdvancedExtensions().UseYamlFrontMatter().DisableHtml().Build());
    }

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

    [Fact]
    public void Does_not_render_the_yaml_front_matter_block()
    {
        // Arrange
        RegisterPipeline();
        const string markdown = "---\ntopics:\n  - orders.*.placed\n---\n# Orders placed\n";

        // Act
        var cut = Render<MarkdownContent>(ps => ps
            .Add(p => p.Markdown, markdown)
            .Add(p => p.Path, "topics/orders-shared.md"));

        // Assert — the front-matter keys are omitted; the heading renders
        Assert.DoesNotContain("orders.*.placed", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("<h1", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Orders placed", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Highlights_rendered_markdown_via_prism_after_render()
    {
        // Arrange
        RegisterPipeline();

        // Act
        var cut = Render<MarkdownContent>(ps => ps
            .Add(p => p.Markdown, "```json\n{ \"id\": 42 }\n```")
            .Add(p => p.Path, "topics/orders.md"));

        // Assert — the language class is emitted and Prism was triggered once on the rendered body
        Assert.Contains("language-json", cut.Markup, StringComparison.Ordinal);
        Assert.Single(JSInterop.Invocations, i => string.Equals(i.Identifier, "Prism.highlightAllUnder", StringComparison.Ordinal));
    }

    [Fact]
    public void Does_not_trigger_prism_when_no_markdown_is_present()
    {
        // Arrange
        RegisterPipeline();

        // Act
        _ = Render<MarkdownContent>(ps => ps
            .Add(p => p.Markdown, (string?)null)
            .Add(p => p.Path, "users/svc-payments.md"));

        // Assert — the empty-state branch renders no code body, so Prism is never invoked
        Assert.DoesNotContain(JSInterop.Invocations, i => string.Equals(i.Identifier, "Prism.highlightAllUnder", StringComparison.Ordinal));
    }
}
