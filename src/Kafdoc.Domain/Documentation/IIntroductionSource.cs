namespace Kafdoc.Domain.Documentation;

/// <summary>Reads the operator-authored introduction markdown shown on the Topics landing page.</summary>
public interface IIntroductionSource
{
    /// <summary>Reads the introduction markdown.</summary>
    /// <returns>The raw markdown, or <c>null</c> when no file exists or it cannot be read.</returns>
    string? Read();
}
