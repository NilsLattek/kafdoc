using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Kafdoc.Domain.Documentation;

namespace Kafdoc.Infrastructure.Documentation;

/// <summary>Reads the introduction markdown from <c>index.md</c> at the documentation root.</summary>
public sealed partial class FileIntroductionSource : IIntroductionSource
{
    private const string FileName = "index.md";

    private readonly string _path;
    private readonly ILogger<FileIntroductionSource> _logger;

    /// <summary>Creates the source.</summary>
    /// <param name="options">Documentation location options; the root that holds <c>index.md</c>.</param>
    /// <param name="environment">The host environment, used to resolve a relative root.</param>
    /// <param name="logger">The logger.</param>
    public FileIntroductionSource(
        IOptions<DocumentationOptions> options,
        IHostEnvironment environment,
        ILogger<FileIntroductionSource> logger)
    {
        var rootPath = options.Value.RootPath;
        var root = Path.IsPathRooted(rootPath) ? rootPath : Path.Combine(environment.ContentRootPath, rootPath);
        _path = Path.Combine(root, FileName);
        _logger = logger;
    }

    /// <inheritdoc />
    public string? Read()
    {
        try
        {
            return File.Exists(_path) ? File.ReadAllText(_path) : null;
        }
        catch (IOException ex)
        {
            LogReadFailed(_logger, _path, ex);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            LogReadFailed(_logger, _path, ex);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read introduction file {Path}")]
    private static partial void LogReadFailed(ILogger logger, string path, Exception ex);
}
