namespace Kafdoc.Application.Dtos;

/// <summary>Full detail of a single topic: producers and consumers.</summary>
/// <param name="Name">The topic name.</param>
/// <param name="PartitionCount">The number of partitions.</param>
/// <param name="Producers">Principals permitted to produce (WRITE ACL).</param>
/// <param name="ConsumerGroups">Consumer groups actually consuming the topic.</param>
/// <param name="ReadOnlyPrincipals">Principals permitted to read the topic but not tied to a consuming group.</param>
/// <param name="DocumentationPath">The expected documentation file path, always set (e.g. <c>topics/orders.placed.md</c>).</param>
/// <param name="Documentation">The raw markdown for the topic, or <c>null</c> if no file exists.</param>
public sealed record TopicDetailDto(
    string Name,
    int PartitionCount,
    IReadOnlyList<string> Producers,
    IReadOnlyList<TopicConsumerDto> ConsumerGroups,
    IReadOnlyList<string> ReadOnlyPrincipals,
    string DocumentationPath,
    string? Documentation);
