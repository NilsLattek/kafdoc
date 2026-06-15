using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

using Kafdoc.Domain.Documentation;
using Kafdoc.Infrastructure.Documentation;

namespace Kafdoc.InfrastructureTest.Documentation;

public sealed class FileDocumentationStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kafdoc-doctests-" + Guid.NewGuid().ToString("N"));

    private FileDocumentationStore CreateStore()
    {
        var options = Options.Create(new DocumentationOptions { RootPath = _root });
        var env = Substitute.For<IHostEnvironment>();
        env.ContentRootPath.Returns(_root);
        return new FileDocumentationStore(options, env, NullLogger<FileDocumentationStore>.Instance);
    }

    private void WriteDoc(string subfolder, string fileName, string content)
    {
        var dir = Path.Combine(_root, subfolder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), content);
    }

    [Fact]
    public void Read_returns_content_and_path_for_an_existing_topic_file()
    {
        // Arrange
        WriteDoc("topics", "orders.placed.md", "# Orders");
        var store = CreateStore();

        // Act
        var result = store.Read(DocumentationKind.Topic, "orders.placed");

        // Assert
        Assert.Equal("topics/orders.placed.md", result.RelativePath);
        Assert.Equal("# Orders", result.Content);
    }

    [Fact]
    public void Read_strips_user_prefix_when_locating_a_user_file()
    {
        // Arrange
        WriteDoc("users", "svc-payments.md", "# Payments service");
        var store = CreateStore();

        // Act
        var result = store.Read(DocumentationKind.User, "User:svc-payments");

        // Assert
        Assert.Equal("users/svc-payments.md", result.RelativePath);
        Assert.Equal("# Payments service", result.Content);
    }

    [Fact]
    public void Read_returns_null_content_but_keeps_path_when_file_is_missing()
    {
        // Arrange
        var store = CreateStore();

        // Act
        var result = store.Read(DocumentationKind.Topic, "absent");

        // Assert
        Assert.Equal("topics/absent.md", result.RelativePath);
        Assert.Null(result.Content);
    }

    [Fact]
    public void Read_blocks_path_traversal_outside_the_folder()
    {
        // Arrange — also drop a file where a naive join might land
        WriteDoc("users", "secret.md", "secret");
        var store = CreateStore();

        // Act
        var result = store.Read(DocumentationKind.Topic, "../users/secret");

        // Assert — slug neutralizes separators, so nothing is read
        Assert.Null(result.Content);
    }

    [Fact]
    public void ListSlugs_returns_file_names_without_extension()
    {
        // Arrange
        WriteDoc("topics", "a.md", "x");
        WriteDoc("topics", "b.md", "y");
        var store = CreateStore();

        // Act
        var slugs = store.ListSlugs(DocumentationKind.Topic).OrderBy(s => s, StringComparer.Ordinal).ToList();

        // Assert
        Assert.Equal(["a", "b"], slugs);
    }

    [Fact]
    public void ListSlugs_returns_empty_when_the_directory_is_absent()
    {
        // Arrange
        var store = CreateStore();

        // Act + Assert
        Assert.Empty(store.ListSlugs(DocumentationKind.User));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
