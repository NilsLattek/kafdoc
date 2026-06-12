namespace Kafdoc.Domain.Graph;

/// <summary>A topic node in the cluster graph.</summary>
/// <param name="Name">The topic name.</param>
/// <param name="PartitionCount">The number of partitions.</param>
public sealed record KafkaTopic(string Name, int PartitionCount);
