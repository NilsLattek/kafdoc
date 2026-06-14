using Confluent.Kafka;
using Confluent.Kafka.Admin;

using Microsoft.Extensions.Options;

using Kafdoc.Domain.Kafka;

namespace Kafdoc.Infrastructure.Kafka;

/// <summary>
/// Reads raw facts from a Kafka cluster using the Confluent.Kafka Admin API.
/// Fetches and maps only; all graph derivation lives in the Domain layer.
/// </summary>
internal sealed class ConfluentKafkaClusterReader(
    IAdminClient adminClient,
    IOptions<KafkaConnectionOptions> options) : IKafkaClusterReader
{
    private readonly TimeSpan _timeout = options.Value.RequestTimeout;

    /// <inheritdoc />
    public async Task<RawClusterData> ReadAsync(CancellationToken cancellationToken)
    {
        var topics = ReadTopics();

        cancellationToken.ThrowIfCancellationRequested();
        var acls = await ReadAclsAsync().ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var groups = await ReadConsumerGroupsAsync().ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var scramUsers = await ReadScramUsersAsync().ConfigureAwait(false);

        return new RawClusterData(topics, acls, groups, scramUsers);
    }

    private List<RawTopic> ReadTopics()
    {
        var metadata = adminClient.GetMetadata(_timeout);
        return metadata.Topics
            .Where(t => !t.Topic.StartsWith("__", StringComparison.Ordinal)) // skip internal topics
            .Select(t => new RawTopic(t.Topic, t.Partitions.Count))
            .ToList();
    }

    private async Task<IReadOnlyList<RawAcl>> ReadAclsAsync()
    {
        var filter = new AclBindingFilter
        {
            PatternFilter = new ResourcePatternFilter
            {
                Type = ResourceType.Any,
                ResourcePatternType = ResourcePatternType.Any,
            },
            EntryFilter = new AccessControlEntryFilter
            {
                Operation = AclOperation.Any,
                PermissionType = AclPermissionType.Any,
            },
        };

        var result = await adminClient
            .DescribeAclsAsync(filter, new DescribeAclsOptions { RequestTimeout = _timeout })
            .ConfigureAwait(false);

        return result.AclBindings.Select(MapAcl).ToList();
    }

    private async Task<IReadOnlyList<RawConsumerGroup>> ReadConsumerGroupsAsync()
    {
        var listing = await adminClient
            .ListConsumerGroupsAsync(new ListConsumerGroupsOptions { RequestTimeout = _timeout })
            .ConfigureAwait(false);

        var groupIds = listing.Valid.Select(g => g.GroupId).ToList();
        if (groupIds.Count == 0)
        {
            return [];
        }

        var described = await adminClient
            .DescribeConsumerGroupsAsync(groupIds, new DescribeConsumerGroupsOptions { RequestTimeout = _timeout })
            .ConfigureAwait(false);

        // Confluent.Kafka's IAdminClient only supports listing offsets for one group per
        // call ("Can only list offsets for one group at a time"), so query each separately.
        var topicsByGroup = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var groupId in groupIds)
        {
            var result = await adminClient
                .ListConsumerGroupOffsetsAsync(
                    [new ConsumerGroupTopicPartitions(groupId, null)],
                    new ListConsumerGroupOffsetsOptions { RequestTimeout = _timeout })
                .ConfigureAwait(false);

            foreach (var offsets in result)
            {
                topicsByGroup[offsets.Group] = offsets.Partitions
                    .Where(p => p.Offset != Offset.Unset)
                    .Select(p => p.TopicPartition.Topic)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }
        }

        return described.ConsumerGroupDescriptions
            .Select(d => new RawConsumerGroup(
                d.GroupId,
                d.State.ToString(),
                d.Members.Count,
                topicsByGroup.GetValueOrDefault(d.GroupId, [])))
            .ToList();
    }

    private async Task<IReadOnlyList<RawScramUser>> ReadScramUsersAsync()
    {
        // Passing an empty list describes all users with SCRAM credentials.
        var result = await adminClient
            .DescribeUserScramCredentialsAsync([], new DescribeUserScramCredentialsOptions { RequestTimeout = _timeout })
            .ConfigureAwait(false);

        return result.UserScramCredentialsDescriptions
            .Select(d => new RawScramUser($"User:{d.User}"))
            .ToList();
    }

    private static RawAcl MapAcl(AclBinding binding) => new(
        binding.Entry.Principal,
        MapResourceType(binding.Pattern.Type),
        binding.Pattern.Name,
        MapPatternType(binding.Pattern.ResourcePatternType),
        MapOperation(binding.Entry.Operation),
        MapPermission(binding.Entry.PermissionType));

    private static KafkaResourceType MapResourceType(ResourceType type) => type switch
    {
        ResourceType.Topic => KafkaResourceType.Topic,
        ResourceType.Group => KafkaResourceType.Group,
        ResourceType.Broker => KafkaResourceType.Cluster,
        _ => KafkaResourceType.Other,
    };

    private static KafkaResourcePatternType MapPatternType(ResourcePatternType type) => type switch
    {
        ResourcePatternType.Literal => KafkaResourcePatternType.Literal,
        ResourcePatternType.Prefixed => KafkaResourcePatternType.Prefixed,
        _ => KafkaResourcePatternType.Other,
    };

    private static KafkaAclOperation MapOperation(AclOperation op) => op switch
    {
        AclOperation.Read => KafkaAclOperation.Read,
        AclOperation.Write => KafkaAclOperation.Write,
        AclOperation.All => KafkaAclOperation.All,
        _ => KafkaAclOperation.Other,
    };

    private static KafkaAclPermission MapPermission(AclPermissionType permission) => permission switch
    {
        AclPermissionType.Allow => KafkaAclPermission.Allow,
        AclPermissionType.Deny => KafkaAclPermission.Deny,
        _ => KafkaAclPermission.Other,
    };
}
