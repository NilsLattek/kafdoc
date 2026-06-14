namespace Kafdoc.Domain.Kafka;

/// <summary>
/// Reduces <see cref="RawClusterData"/> to a single logical environment by keeping
/// only resources whose names match the configured prefixes. Pure and deterministic:
/// it performs no I/O and depends only on its input and options.
/// </summary>
/// <param name="options">The prefix allow-lists per resource type.</param>
public sealed class RawClusterDataFilter(ClusterFilterOptions options)
{
    private const string PrincipalTypePrefix = "User:";

    /// <summary>
    /// Applies the configured prefix allow-lists, returning a reduced copy of the
    /// raw data. Resource-side ACL pruning is left to the graph builder, which only
    /// matches ACL resources against the surviving topic and group names.
    /// </summary>
    /// <param name="raw">The raw cluster facts to filter.</param>
    /// <returns>A reduced <see cref="RawClusterData"/>.</returns>
    public RawClusterData Apply(RawClusterData raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var topics = raw.Topics
            .Where(t => Matches(t.Name, options.TopicPrefixes))
            .ToList();

        var acls = raw.Acls
            .Where(a => Matches(PrincipalName(a.Principal), options.UserPrefixes))
            .ToList();

        var scramUsers = raw.ScramUsers
            .Where(u => Matches(PrincipalName(u.Principal), options.UserPrefixes))
            .ToList();

        var groups = raw.ConsumerGroups
            .Where(g => Matches(g.GroupId, options.GroupPrefixes))
            .Select(g => g with
            {
                ConsumedTopics = g.ConsumedTopics
                    .Where(t => Matches(t, options.TopicPrefixes))
                    .ToList(),
            })
            .ToList();

        return new RawClusterData(topics, acls, groups, scramUsers);
    }

    private static bool Matches(string value, IReadOnlyList<string> prefixes) =>
        prefixes.Count == 0
        || prefixes.Any(p => value.StartsWith(p, StringComparison.Ordinal));

    private static string PrincipalName(string principal) =>
        principal.StartsWith(PrincipalTypePrefix, StringComparison.Ordinal)
            ? principal[PrincipalTypePrefix.Length..]
            : principal;
}
