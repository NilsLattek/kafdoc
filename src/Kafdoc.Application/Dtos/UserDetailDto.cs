namespace Kafdoc.Application.Dtos;

/// <summary>Full detail of a single user (principal).</summary>
/// <param name="Principal">The principal.</param>
/// <param name="HasScramCredentials">Whether the principal has SCRAM credentials.</param>
/// <param name="ProducesTopics">Topics the principal may produce to.</param>
/// <param name="ConsumesTopics">Topics the principal may consume.</param>
/// <param name="Groups">Consumer groups the principal backs.</param>
public sealed record UserDetailDto(
    string Principal,
    bool HasScramCredentials,
    IReadOnlyList<string> ProducesTopics,
    IReadOnlyList<string> ConsumesTopics,
    IReadOnlyList<string> Groups);
