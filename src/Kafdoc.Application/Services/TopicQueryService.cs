using Kafdoc.Application.Dtos;
using Kafdoc.Application.Snapshot;
using Kafdoc.Domain.Documentation;
using Kafdoc.Domain.Graph;

namespace Kafdoc.Application.Services;

/// <summary>Computes topic views from the current snapshot.</summary>
internal sealed class TopicQueryService(ISnapshotStore store, IDocumentationStore documentation) : ITopicQueryService
{
    /// <inheritdoc />
    public IReadOnlyList<TopicSummaryDto> GetTopics()
    {
        var graph = store.Current?.Graph;
        if (graph is null)
        {
            return [];
        }

        var producersByTopic = graph.Producers
            .GroupBy(p => p.Topic, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Principal).Distinct(StringComparer.Ordinal).Count(), StringComparer.Ordinal);

        var groupsByTopic = graph.GroupConsumption
            .GroupBy(e => e.Topic, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.GroupId).Distinct(StringComparer.Ordinal).Count(), StringComparer.Ordinal);

        var docSlugs = documentation.ListSlugs(DocumentationKind.Topic);

        return graph.Topics
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new TopicSummaryDto(
                t.Name,
                t.PartitionCount,
                producersByTopic.GetValueOrDefault(t.Name),
                groupsByTopic.GetValueOrDefault(t.Name),
                docSlugs.Contains(DocumentationSlug.ForTopic(t.Name))))
            .ToList();
    }

    /// <inheritdoc />
    public TopicDetailDto? GetTopic(string name)
    {
        var graph = store.Current?.Graph;
        var topic = graph?.Topics.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
        if (graph is null || topic is null)
        {
            return null;
        }

        var producers = graph.Producers
            .Where(p => string.Equals(p.Topic, name, StringComparison.Ordinal))
            .Select(p => p.Principal)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var consumingGroupIds = graph.GroupConsumption
            .Where(e => string.Equals(e.Topic, name, StringComparison.Ordinal))
            .Select(e => e.GroupId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var principalsByGroup = graph.UserGroups
            .GroupBy(e => e.GroupId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Principal).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

        var groupStates = graph.ConsumerGroups.ToDictionary(g => g.GroupId, g => g.State, StringComparer.Ordinal);

        var consumerGroups = consumingGroupIds
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => new TopicConsumerDto(
                id,
                groupStates.GetValueOrDefault(id, "Unknown"),
                principalsByGroup.GetValueOrDefault(id, [])))
            .ToList();

        var principalsInGroups = principalsByGroup
            .Where(kvp => consumingGroupIds.Contains(kvp.Key, StringComparer.Ordinal))
            .SelectMany(kvp => kvp.Value)
            .ToHashSet(StringComparer.Ordinal);

        var readOnlyPrincipals = graph.Consumers
            .Where(c => string.Equals(c.Topic, name, StringComparison.Ordinal))
            .Select(c => c.Principal)
            .Where(p => !principalsInGroups.Contains(p))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var doc = documentation.Read(DocumentationKind.Topic, name);

        return new TopicDetailDto(topic.Name, topic.PartitionCount, producers, consumerGroups, readOnlyPrincipals, doc.RelativePath, doc.Content);
    }
}
