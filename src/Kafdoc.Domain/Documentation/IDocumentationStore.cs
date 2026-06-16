namespace Kafdoc.Domain.Documentation;

/// <summary>Reads operator-authored markdown documentation for topics and users.</summary>
public interface IDocumentationStore
{
    /// <summary>Looks up documentation for a single entity.</summary>
    /// <param name="kind">Whether the name is a topic or a user.</param>
    /// <param name="name">The topic name or principal.</param>
    /// <returns>The lookup; <see cref="DocumentationLookup.Content"/> is <c>null</c> when no file exists.</returns>
    DocumentationLookup Read(DocumentationKind kind, string name);

    /// <summary>Indicates whether the name resolves to a documentation file (own slug or alias).</summary>
    /// <param name="kind">Whether the name is a topic or a user.</param>
    /// <param name="name">The topic name or principal.</param>
    /// <returns><c>true</c> when documentation exists; an index lookup with no content load.</returns>
    bool HasDocumentation(DocumentationKind kind, string name);
}
