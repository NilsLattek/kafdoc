using Kafdoc.Domain.Documentation;

namespace Kafdoc.DomainTest.Documentation;

public class DocumentationPatternTests
{
    [Fact]
    public void Matches_star_spans_dots()
    {
        // Act + Assert
        Assert.True(DocumentationPattern.Matches("orders.*.placed", "orders.branch01.placed"));
        Assert.True(DocumentationPattern.Matches("orders.*.placed", "orders.branch07.placed"));
    }

    [Fact]
    public void Matches_exact_when_no_star()
    {
        // Act + Assert
        Assert.True(DocumentationPattern.Matches("legacy.orders.placed", "legacy.orders.placed"));
        Assert.False(DocumentationPattern.Matches("legacy.orders.placed", "legacy.orders.created"));
    }

    [Fact]
    public void Matches_is_case_sensitive_ordinal()
    {
        // Act + Assert
        Assert.False(DocumentationPattern.Matches("Orders.placed", "orders.placed"));
    }

    [Fact]
    public void Matches_leading_and_trailing_star()
    {
        // Act + Assert
        Assert.True(DocumentationPattern.Matches("*.placed", "orders.branch01.placed"));
        Assert.True(DocumentationPattern.Matches("orders.*", "orders.branch01.placed"));
    }

    [Fact]
    public void Matches_returns_false_on_mismatch()
    {
        // Act + Assert
        Assert.False(DocumentationPattern.Matches("orders.*.placed", "orders.branch01.created"));
    }

    [Fact]
    public void Matches_handles_empty_inputs()
    {
        // Act + Assert
        Assert.True(DocumentationPattern.Matches("", ""));
        Assert.True(DocumentationPattern.Matches("*", ""));
        Assert.False(DocumentationPattern.Matches("", "x"));
    }
}
