namespace Kafdoc.Domain.Documentation;

/// <summary>The result of a documentation lookup.</summary>
/// <param name="RelativePath">The expected file path relative to the docs root, always populated (e.g. <c>topics/orders.placed.md</c>).</param>
/// <param name="Content">The raw markdown, or <c>null</c> when no file exists.</param>
public sealed record DocumentationLookup(string RelativePath, string? Content);
