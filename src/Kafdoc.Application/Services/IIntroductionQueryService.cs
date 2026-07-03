namespace Kafdoc.Application.Services;

/// <summary>Provides the introduction markdown for the Topics landing page.</summary>
public interface IIntroductionQueryService
{
    /// <summary>Gets the introduction markdown.</summary>
    /// <returns>The raw markdown, or <c>null</c> when none is authored.</returns>
    string? GetIntroduction();
}
