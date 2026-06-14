namespace Kafdoc.Application.Dtos;

/// <summary>A consumer group consuming a topic, with the principals that back it.</summary>
/// <param name="GroupId">The consumer group id.</param>
/// <param name="State">The group state.</param>
/// <param name="Principals">Principals tied to the group via group-resource READ ACLs.</param>
public sealed record TopicConsumerDto(string GroupId, string State, IReadOnlyList<string> Principals);
