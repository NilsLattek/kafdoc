namespace Kafdoc.Domain.Kafka;

/// <summary>A topic as read from cluster metadata.</summary>
/// <param name="Name">The topic name.</param>
/// <param name="PartitionCount">The number of partitions.</param>
public sealed record RawTopic(string Name, int PartitionCount);
