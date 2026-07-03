using Kafdoc.Domain.Documentation;

namespace Kafdoc.Application.Services;

/// <summary>Reads the introduction markdown from the documentation source.</summary>
/// <param name="source">The introduction source.</param>
internal sealed class IntroductionQueryService(IIntroductionSource source) : IIntroductionQueryService
{
    /// <inheritdoc />
    public string? GetIntroduction() => source.Read();
}
