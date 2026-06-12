namespace Kafdoc.Application.Dtos;

/// <summary>Summary of a topic for the topics list.</summary>
/// <param name="Name">The topic name.</param>
/// <param name="PartitionCount">The number of partitions.</param>
/// <param name="ProducerCount">The number of distinct producing principals.</param>
/// <param name="ConsumerGroupCount">The number of consumer groups consuming the topic.</param>
public sealed record TopicSummaryDto(string Name, int PartitionCount, int ProducerCount, int ConsumerGroupCount);
