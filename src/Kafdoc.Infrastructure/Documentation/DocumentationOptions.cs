namespace Kafdoc.Infrastructure.Documentation;

/// <summary>Settings for locating operator-authored documentation files.</summary>
public sealed class DocumentationOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Documentation";

    /// <summary>
    /// Root folder holding the <c>topics/</c> and <c>users/</c> subfolders, relative to the
    /// content root (or an absolute path). Empty means the content root itself.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;
}
