using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

using Kafdoc.Infrastructure.Documentation;

namespace Kafdoc.InfrastructureTest.Documentation;

public sealed class FileIntroductionSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kafdoc-introtests-" + Guid.NewGuid().ToString("N"));

    private FileIntroductionSource CreateSource(ILogger<FileIntroductionSource>? logger = null)
    {
        var options = Options.Create(new DocumentationOptions { RootPath = _root });
        var env = Substitute.For<IHostEnvironment>();
        env.ContentRootPath.Returns(_root);
        return new FileIntroductionSource(options, env, logger ?? NullLogger<FileIntroductionSource>.Instance);
    }

    private void WriteIndex(string content)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "index.md"), content);
    }

    [Fact]
    public void Read_returns_content_when_index_file_exists()
    {
        // Arrange
        WriteIndex("# Welcome\n\n[repo](https://example.com)");
        var source = CreateSource();

        // Act
        var result = source.Read();

        // Assert
        Assert.Equal("# Welcome\n\n[repo](https://example.com)", result);
    }

    [Fact]
    public void Read_returns_null_when_index_file_missing()
    {
        // Arrange
        var source = CreateSource();

        // Act
        var result = source.Read();

        // Assert
        Assert.Null(result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
