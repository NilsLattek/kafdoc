namespace Kafdoc.Infrastructure.Documentation;

/// <summary>
/// An immutable, content-free map from documentation slugs and front-matter glob
/// patterns to the relative file paths that satisfy them. Built once per kind and
/// swapped atomically.
/// </summary>
internal sealed class DocumentationIndex
{
    /// <summary>Creates the index.</summary>
    /// <param name="bySlug">Filename slug (no folder, no extension) to relative path, e.g. <c>orders.placed</c> → <c>topics/orders.placed.md</c>.</param>
    /// <param name="patterns">Ordered (glob, relative path) pairs gathered from front matter, sorted by relative path so resolution is a deterministic first-match.</param>
    public DocumentationIndex(
        IReadOnlyDictionary<string, string> bySlug,
        IReadOnlyList<(string Pattern, string RelativePath)> patterns)
    {
        BySlug = bySlug;
        Patterns = patterns;
    }

    /// <summary>Filename slug to relative path.</summary>
    public IReadOnlyDictionary<string, string> BySlug { get; }

    /// <summary>Ordered front-matter (glob, relative path) pairs; first match wins.</summary>
    public IReadOnlyList<(string Pattern, string RelativePath)> Patterns { get; }
}
