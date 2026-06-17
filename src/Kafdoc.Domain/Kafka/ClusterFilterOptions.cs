namespace Kafdoc.Domain.Kafka;

/// <summary>
/// Prefix allow-lists that restrict the documented cluster to a single logical
/// environment. An empty list for a resource type keeps every resource of that type.
/// </summary>
public sealed class ClusterFilterOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Kafka:Filter";

    /// <summary>Name prefixes of topics to keep. Empty keeps all topics.</summary>
    public IReadOnlyList<string> TopicPrefixes { get; set; } = [];

    /// <summary>
    /// Principal-name prefixes of users to keep, matched after the <c>User:</c>
    /// type prefix is stripped. Empty keeps all users.
    /// </summary>
    public IReadOnlyList<string> UserPrefixes { get; set; } = [];

    /// <summary>Name prefixes of consumer groups to keep. Empty keeps all groups.</summary>
    public IReadOnlyList<string> GroupPrefixes { get; set; } = [];

    /// <summary>
    /// Principal names to exclude entirely, matched after the <c>User:</c> type
    /// prefix is stripped (exact, ordinal). Their ACLs and SCRAM entries are
    /// dropped, removing their nodes and edges from the graph. Empty excludes nobody.
    /// </summary>
    public IReadOnlyList<string> ExcludedUsers { get; set; } = [];
}
