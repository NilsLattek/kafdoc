namespace Kafdoc.Domain.Documentation;

/// <summary>The kind of entity a documentation file describes.</summary>
public enum DocumentationKind
{
    /// <summary>A Kafka topic.</summary>
    Topic,

    /// <summary>A Kafka user (principal).</summary>
    User,
}
