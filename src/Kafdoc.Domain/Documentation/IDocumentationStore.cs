namespace Kafdoc.Domain.Documentation;

/// <summary>Reads operator-authored markdown documentation for topics and users.</summary>
public interface IDocumentationStore
{
    /// <summary>Looks up documentation for a single entity.</summary>
    /// <param name="kind">Whether the name is a topic or a user.</param>
    /// <param name="name">The topic name or principal.</param>
    /// <returns>The lookup; <see cref="DocumentationLookup.Content"/> is <c>null</c> when no file exists.</returns>
    DocumentationLookup Read(DocumentationKind kind, string name);

    /// <summary>Lists the slugs that currently have a documentation file on disk.</summary>
    /// <param name="kind">Whether to list topic or user docs.</param>
    /// <returns>Slugs (file names without extension) present in the relevant folder.</returns>
    IReadOnlySet<string> ListSlugs(DocumentationKind kind);
}
