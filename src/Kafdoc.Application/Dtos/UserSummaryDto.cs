namespace Kafdoc.Application.Dtos;

/// <summary>Summary of a user (principal) for the users list.</summary>
/// <param name="Principal">The principal.</param>
/// <param name="HasScramCredentials">Whether the principal has SCRAM credentials.</param>
/// <param name="ProducesCount">Number of topics the principal may produce to.</param>
/// <param name="ConsumesCount">Number of topics the principal may consume.</param>
/// <param name="HasDocumentation">Whether a documentation file exists for the user.</param>
public sealed record UserSummaryDto(string Principal, bool HasScramCredentials, int ProducesCount, int ConsumesCount, bool HasDocumentation);
