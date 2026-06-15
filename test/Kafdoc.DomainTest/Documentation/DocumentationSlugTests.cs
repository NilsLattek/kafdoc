using Kafdoc.Domain.Documentation;

namespace Kafdoc.DomainTest.Documentation;

public class DocumentationSlugTests
{
    [Fact]
    public void ForTopic_keeps_dots_in_the_name()
    {
        // Act
        var slug = DocumentationSlug.ForTopic("orders.placed");

        // Assert
        Assert.Equal("orders.placed", slug);
    }

    [Fact]
    public void ForUser_strips_the_user_prefix()
    {
        // Act
        var slug = DocumentationSlug.ForUser("User:svc-payments");

        // Assert
        Assert.Equal("svc-payments", slug);
    }

    [Fact]
    public void ForUser_without_prefix_uses_the_whole_name()
    {
        // Act
        var slug = DocumentationSlug.ForUser("svc-payments");

        // Assert
        Assert.Equal("svc-payments", slug);
    }

    [Fact]
    public void ForTopic_replaces_filesystem_illegal_characters_with_underscore()
    {
        // Act — colon, slash, backslash, pipe are illegal
        var slug = DocumentationSlug.ForTopic("a:b/c\\d|e");

        // Assert
        Assert.Equal("a_b_c_d_e", slug);
    }

    [Fact]
    public void ForTopic_strips_leading_dots_to_neutralize_traversal()
    {
        // Act
        var slug = DocumentationSlug.ForTopic("../etc/passwd");

        // Assert — separators become underscores and leading dots are removed,
        // so the slug can never escape its folder (no leading dots, no separators)
        Assert.Equal("_etc_passwd", slug);
    }

    [Fact]
    public void ForTopic_keeps_a_leading_underscore()
    {
        // Act — single-underscore topics like _schemas must survive intact
        var slug = DocumentationSlug.ForTopic("_schemas");

        // Assert
        Assert.Equal("_schemas", slug);
    }

    [Fact]
    public void ForTopic_returns_empty_when_the_name_is_all_dots()
    {
        // Act — the file adapter treats an empty slug as "no documentation"
        var slug = DocumentationSlug.ForTopic("..");

        // Assert
        Assert.Equal(string.Empty, slug);
    }
}
