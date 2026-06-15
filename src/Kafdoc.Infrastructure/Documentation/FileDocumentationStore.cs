using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Kafdoc.Domain.Documentation;

namespace Kafdoc.Infrastructure.Documentation;

/// <summary>Reads markdown documentation files from disk on demand.</summary>
public sealed partial class FileDocumentationStore : IDocumentationStore
{
    private readonly string _root;
    private readonly ILogger<FileDocumentationStore> _logger;

    /// <summary>Creates the store.</summary>
    /// <param name="options">Documentation location options.</param>
    /// <param name="environment">The host environment, used to resolve a relative root.</param>
    /// <param name="logger">The logger.</param>
    public FileDocumentationStore(
        IOptions<DocumentationOptions> options,
        IHostEnvironment environment,
        ILogger<FileDocumentationStore> logger)
    {
        var rootPath = options.Value.RootPath;
        _root = Path.IsPathRooted(rootPath) ? rootPath : Path.Combine(environment.ContentRootPath, rootPath);
        _logger = logger;
    }

    /// <inheritdoc />
    public DocumentationLookup Read(DocumentationKind kind, string name)
    {
        var folder = Folder(kind);
        var slug = Slug(kind, name);
        var relativePath = $"{folder}/{slug}.md";

        if (string.IsNullOrEmpty(slug))
        {
            return new DocumentationLookup(relativePath, null);
        }

        var directory = Path.GetFullPath(Path.Combine(_root, folder));
        var fullPath = Path.GetFullPath(Path.Combine(directory, slug + ".md"));

        // Defense in depth: ensure the resolved path stays inside the intended folder.
        if (!fullPath.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return new DocumentationLookup(relativePath, null);
        }

        try
        {
            var content = File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
            return new DocumentationLookup(relativePath, content);
        }
        catch (IOException ex)
        {
            LogReadFailed(_logger, fullPath, ex);
            return new DocumentationLookup(relativePath, null);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogReadFailed(_logger, fullPath, ex);
            return new DocumentationLookup(relativePath, null);
        }
    }

    /// <inheritdoc />
    public IReadOnlySet<string> ListSlugs(DocumentationKind kind)
    {
        var directory = Path.Combine(_root, Folder(kind));
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(directory))
        {
            return result;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.md"))
            {
                var slug = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrEmpty(slug))
                {
                    result.Add(slug);
                }
            }
        }
        catch (IOException ex)
        {
            LogListFailed(_logger, directory, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogListFailed(_logger, directory, ex);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read documentation file {Path}")]
    private static partial void LogReadFailed(ILogger logger, string path, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to list documentation in {Directory}")]
    private static partial void LogListFailed(ILogger logger, string directory, Exception ex);

    private static string Folder(DocumentationKind kind) => kind == DocumentationKind.Topic ? "topics" : "users";

    private static string Slug(DocumentationKind kind, string name) =>
        kind == DocumentationKind.Topic ? DocumentationSlug.ForTopic(name) : DocumentationSlug.ForUser(name);
}
