using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

using Kafdoc.Domain.Documentation;

namespace Kafdoc.Infrastructure.Documentation;

/// <summary>Resolves markdown documentation files via a lazily-built immutable index.</summary>
public sealed partial class FileDocumentationStore : IDocumentationStore
{
    private const string UserPrefix = "User:";

    private static readonly MarkdownPipeline FrontMatterPipeline =
        new MarkdownPipelineBuilder().UseYamlFrontMatter().Build();

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly string _root;
    private readonly ILogger<FileDocumentationStore> _logger;

    private volatile DocumentationIndex? _topicIndex;
    private volatile DocumentationIndex? _userIndex;

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
        var ownPath = $"{folder}/{slug}.md";

        var resolved = Resolve(kind, name);
        if (resolved is null)
        {
            return new DocumentationLookup(ownPath, null);
        }

        var directory = Path.GetFullPath(Path.Combine(_root, folder));
        var fullPath = Path.GetFullPath(Path.Combine(_root, resolved));

        // Defense in depth: ensure the resolved path stays inside the intended folder.
        if (!fullPath.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return new DocumentationLookup(ownPath, null);
        }

        try
        {
            var content = File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
            return content is null
                ? new DocumentationLookup(ownPath, null)
                : new DocumentationLookup(resolved, content);
        }
        catch (IOException ex)
        {
            LogReadFailed(_logger, fullPath, ex);
            return new DocumentationLookup(ownPath, null);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogReadFailed(_logger, fullPath, ex);
            return new DocumentationLookup(ownPath, null);
        }
    }

    /// <inheritdoc />
    public bool HasDocumentation(DocumentationKind kind, string name) => Resolve(kind, name) is not null;

    /// <summary>Rebuilds both kind indexes from disk and swaps them in atomically.</summary>
    public void Rebuild()
    {
        _topicIndex = BuildIndex(DocumentationKind.Topic);
        _userIndex = BuildIndex(DocumentationKind.User);
    }

    private string? Resolve(DocumentationKind kind, string name)
    {
        var index = GetIndex(kind);

        var slug = Slug(kind, name);
        if (!string.IsNullOrEmpty(slug) && index.BySlug.TryGetValue(slug, out var ownPath))
        {
            return ownPath;
        }

        var raw = RawName(kind, name);
        string? firstMatch = null;
        foreach (var (pattern, relativePath) in index.Patterns)
        {
            if (!DocumentationPattern.Matches(pattern, raw))
            {
                continue;
            }

            if (firstMatch is null)
            {
                firstMatch = relativePath;
            }
            else if (!string.Equals(firstMatch, relativePath, StringComparison.Ordinal))
            {
                LogMultipleMatches(_logger, raw, firstMatch, relativePath);
                break;
            }
        }

        return firstMatch;
    }

    private DocumentationIndex GetIndex(DocumentationKind kind) => kind == DocumentationKind.Topic
        ? _topicIndex ??= BuildIndex(DocumentationKind.Topic)
        : _userIndex ??= BuildIndex(DocumentationKind.User);

    private DocumentationIndex BuildIndex(DocumentationKind kind)
    {
        var folder = Folder(kind);
        var bySlug = new Dictionary<string, string>(StringComparer.Ordinal);
        var patterns = new List<(string Pattern, string RelativePath)>();
        var directory = Path.Combine(_root, folder);

        if (!Directory.Exists(directory))
        {
            return new DocumentationIndex(bySlug, patterns);
        }

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*.md")
                .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
                .ToList();
        }
        catch (IOException ex)
        {
            LogIndexFailed(_logger, directory, ex);
            return new DocumentationIndex(bySlug, patterns);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogIndexFailed(_logger, directory, ex);
            return new DocumentationIndex(bySlug, patterns);
        }

        foreach (var file in files)
        {
            var slug = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrEmpty(slug))
            {
                continue;
            }

            var relativePath = $"{folder}/{slug}.md";
            bySlug[slug] = relativePath;

            foreach (var alias in ReadAliases(file, kind))
            {
                patterns.Add((alias, relativePath));
            }
        }

        return new DocumentationIndex(bySlug, patterns);
    }

    private List<string> ReadAliases(string file, DocumentationKind kind)
    {
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (IOException ex)
        {
            LogIndexFailed(_logger, file, ex);
            return [];
        }
        catch (UnauthorizedAccessException ex)
        {
            LogIndexFailed(_logger, file, ex);
            return [];
        }

        var block = Markdown.Parse(text, FrontMatterPipeline).Descendants<YamlFrontMatterBlock>().FirstOrDefault();
        if (block is null)
        {
            return [];
        }

        try
        {
            var frontMatter = YamlDeserializer.Deserialize<FrontMatter>(block.Lines.ToString());
            var aliases = kind == DocumentationKind.Topic ? frontMatter?.Topics : frontMatter?.Users;
            return aliases?.Where(a => !string.IsNullOrEmpty(a)).ToList() ?? [];
        }
        catch (YamlException ex)
        {
            LogMalformedFrontMatter(_logger, file, ex);
            return [];
        }
    }

    private static string RawName(DocumentationKind kind, string name) =>
        kind == DocumentationKind.User && name.StartsWith(UserPrefix, StringComparison.Ordinal)
            ? name[UserPrefix.Length..]
            : name;

    private static string Folder(DocumentationKind kind) => kind == DocumentationKind.Topic ? "topics" : "users";

    private static string Slug(DocumentationKind kind, string name) =>
        kind == DocumentationKind.Topic ? DocumentationSlug.ForTopic(name) : DocumentationSlug.ForUser(name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read documentation file {Path}")]
    private static partial void LogReadFailed(ILogger logger, string path, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to index documentation in {Path}")]
    private static partial void LogIndexFailed(ILogger logger, string path, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Malformed documentation front matter in {Path}; aliases ignored")]
    private static partial void LogMalformedFrontMatter(ILogger logger, string path, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Name {Name} matches documentation in multiple files; using {Winner} over {Other}")]
    private static partial void LogMultipleMatches(ILogger logger, string name, string winner, string other);

    // S3459/S1144: properties are set by YamlDotNet via reflection, which the analyzer cannot see.
#pragma warning disable S3459, S1144
    private sealed class FrontMatter
    {
        public List<string>? Topics { get; set; }

        public List<string>? Users { get; set; }
    }
#pragma warning restore S3459, S1144
}
