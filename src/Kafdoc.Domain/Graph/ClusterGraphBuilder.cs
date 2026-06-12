using Kafdoc.Domain.Kafka;

namespace Kafdoc.Domain.Graph;

/// <summary>
/// Builds a <see cref="ClusterGraph"/> from raw Kafka facts. Pure and deterministic:
/// it performs no I/O and depends only on its input.
/// </summary>
public sealed class ClusterGraphBuilder
{
    /// <summary>
    /// Derives the producer/consumer graph from raw cluster data.
    /// </summary>
    /// <param name="raw">The raw topics, ACLs, consumer groups, and SCRAM users.</param>
    /// <returns>The assembled graph.</returns>
#pragma warning disable CA1822 // Build is an instance method for DI/substitution
#pragma warning disable S2325   // Build is an instance method for DI/substitution
    public ClusterGraph Build(RawClusterData raw)
#pragma warning restore S2325
#pragma warning restore CA1822
    {
        ArgumentNullException.ThrowIfNull(raw);

        var topics = raw.Topics
            .Select(t => new KafkaTopic(t.Name, t.PartitionCount))
            .ToList();

        var groups = raw.ConsumerGroups
            .Select(g => new KafkaConsumerGroup(g.GroupId, g.State, g.MemberCount))
            .ToList();

        var topicNames = topics.Select(t => t.Name).ToList();
        var groupIds = groups.Select(g => g.GroupId).ToList();

        var allowAcls = raw.Acls.Where(a => a.Permission == KafkaAclPermission.Allow).ToList();

        var (producers, consumers, userGroups) = BuildEdges(allowAcls, topicNames, groupIds);

        var groupConsumption = BuildGroupTopicEdges(raw.ConsumerGroups);

        var users = BuildUsers(raw.Acls, raw.ScramUsers);

        return new ClusterGraph(
            topics,
            users,
            groups,
            [.. producers],
            [.. consumers],
            [.. userGroups],
            groupConsumption);
    }

    private static (HashSet<ProducerEdge> Producers, HashSet<ConsumerEdge> Consumers, HashSet<UserGroupEdge> UserGroups) BuildEdges(
        IReadOnlyList<RawAcl> allowAcls,
        IReadOnlyList<string> topicNames,
        IReadOnlyList<string> groupIds)
    {
        var producers = new HashSet<ProducerEdge>();
        var consumers = new HashSet<ConsumerEdge>();
        var userGroups = new HashSet<UserGroupEdge>();

        foreach (var acl in allowAcls)
        {
            if (acl.ResourceType == KafkaResourceType.Topic)
            {
                foreach (var topic in MatchResources(acl, topicNames))
                {
                    if (acl.Operation is KafkaAclOperation.Write or KafkaAclOperation.All)
                    {
                        producers.Add(new ProducerEdge(acl.Principal, topic));
                    }

                    if (acl.Operation is KafkaAclOperation.Read or KafkaAclOperation.All)
                    {
                        consumers.Add(new ConsumerEdge(acl.Principal, topic));
                    }
                }
            }
            else if (acl.ResourceType == KafkaResourceType.Group
                && acl.Operation is KafkaAclOperation.Read or KafkaAclOperation.All)
            {
                foreach (var groupId in MatchResources(acl, groupIds))
                {
                    userGroups.Add(new UserGroupEdge(acl.Principal, groupId));
                }
            }
        }

        return (producers, consumers, userGroups);
    }

    private static List<GroupTopicEdge> BuildGroupTopicEdges(IReadOnlyList<RawConsumerGroup> groups) =>
        groups
            .SelectMany(g => g.ConsumedTopics.Select(t => new GroupTopicEdge(g.GroupId, t)))
            .Distinct()
            .ToList();

    private static List<KafkaUser> BuildUsers(IReadOnlyList<RawAcl> acls, IReadOnlyList<RawScramUser> scramUsers)
    {
        var scramPrincipals = scramUsers.Select(u => u.Principal).ToHashSet(StringComparer.Ordinal);
        return acls.Select(a => a.Principal)
            .Concat(scramPrincipals)
            .Distinct(StringComparer.Ordinal)
            .Select(p => new KafkaUser(p, scramPrincipals.Contains(p)))
            .ToList();
    }

    private static IEnumerable<string> MatchResources(RawAcl acl, IReadOnlyList<string> candidates)
    {
        if (acl.PatternType == KafkaResourcePatternType.Prefixed)
        {
            return candidates.Where(c => c.StartsWith(acl.ResourceName, StringComparison.Ordinal));
        }

        // Literal: "*" is the Kafka wildcard meaning all resources; otherwise an exact match.
        if (string.Equals(acl.ResourceName, "*", StringComparison.Ordinal))
        {
            return candidates;
        }

        return candidates.Where(c => string.Equals(c, acl.ResourceName, StringComparison.Ordinal));
    }
}
