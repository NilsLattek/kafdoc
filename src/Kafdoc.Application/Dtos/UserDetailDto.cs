namespace Kafdoc.Application.Dtos;

/// <summary>Full detail of a single user (principal).</summary>
/// <param name="Principal">The principal.</param>
/// <param name="HasScramCredentials">Whether the principal has SCRAM credentials.</param>
/// <param name="ProducesTopics">Topics the principal may produce to.</param>
/// <param name="ConsumesTopics">Topics the principal may consume.</param>
/// <param name="Groups">Consumer groups the principal backs.</param>
/// <param name="DocumentationPath">The expected documentation file path, always set (e.g. <c>users/svc-payments.md</c>).</param>
/// <param name="Documentation">The raw markdown for the user, or <c>null</c> if no file exists.</param>
public sealed record UserDetailDto(
    string Principal,
    bool HasScramCredentials,
    IReadOnlyList<string> ProducesTopics,
    IReadOnlyList<string> ConsumesTopics,
    IReadOnlyList<string> Groups,
    string DocumentationPath,
    string? Documentation);
