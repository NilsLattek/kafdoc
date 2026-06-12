using Kafdoc.Application.Dtos;

namespace Kafdoc.Application.Services;

/// <summary>Read-only queries over the current snapshot's topics.</summary>
public interface ITopicQueryService
{
    /// <summary>Returns all topics as summaries, ordered by name.</summary>
    IReadOnlyList<TopicSummaryDto> GetTopics();

    /// <summary>Returns full detail for one topic, or <see langword="null"/> if it is not present.</summary>
    /// <param name="name">The topic name.</param>
    TopicDetailDto? GetTopic(string name);
}
