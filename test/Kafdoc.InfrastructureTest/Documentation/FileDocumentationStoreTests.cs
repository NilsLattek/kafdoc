using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

using Kafdoc.Domain.Documentation;
using Kafdoc.Infrastructure.Documentation;

namespace Kafdoc.InfrastructureTest.Documentation;

public sealed class FileDocumentationStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kafdoc-doctests-" + Guid.NewGuid().ToString("N"));

    private FileDocumentationStore CreateStore(ILogger<FileDocumentationStore>? logger = null)
    {
        var options = Options.Create(new DocumentationOptions { RootPath = _root });
        var env = Substitute.For<IHostEnvironment>();
        env.ContentRootPath.Returns(_root);
        return new FileDocumentationStore(options, env, logger ?? NullLogger<FileDocumentationStore>.Instance);
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
    public void Read_resolves_a_topic_via_a_front_matter_glob_to_the_shared_file()
    {
        // Arrange
        WriteDoc("topics", "orders-shared.md",
            "---\ntopics:\n  - orders.*.placed\n---\n# Shared orders");
        var store = CreateStore();

        // Act
        var result = store.Read(DocumentationKind.Topic, "orders.branch01.placed");

        // Assert — RelativePath points at the shared file
        Assert.Equal("topics/orders-shared.md", result.RelativePath);
        Assert.Contains("# Shared orders", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_prefers_the_entitys_own_file_over_a_matching_pattern()
    {
        // Arrange — both a shared glob file and the topic's own file exist
        WriteDoc("topics", "orders-shared.md",
            "---\ntopics:\n  - orders.*.placed\n---\n# Shared");
        WriteDoc("topics", "orders.branch01.placed.md", "# Own");
        var store = CreateStore();

        // Act
        var result = store.Read(DocumentationKind.Topic, "orders.branch01.placed");

        // Assert — own file wins
        Assert.Equal("topics/orders.branch01.placed.md", result.RelativePath);
        Assert.Equal("# Own", result.Content);
    }

    [Fact]
    public void Read_parses_aliases_from_a_flow_list()
    {
        // Arrange
        WriteDoc("topics", "shared.md",
            "---\ntopics: [orders.a.placed, orders.b.placed]\n---\n# Shared");
        var store = CreateStore();

        // Act
        var a = store.Read(DocumentationKind.Topic, "orders.a.placed");
        var b = store.Read(DocumentationKind.Topic, "orders.b.placed");

        // Assert
        Assert.Equal("topics/shared.md", a.RelativePath);
        Assert.Equal("topics/shared.md", b.RelativePath);
    }

    [Fact]
    public void Read_matches_user_aliases_against_the_prefix_stripped_principal()
    {
        // Arrange
        WriteDoc("users", "svc-shared.md",
            "---\nusers:\n  - svc-payments-*\n---\n# Shared service");
        var store = CreateStore();

        // Act
        var result = store.Read(DocumentationKind.User, "User:svc-payments-branch07");

        // Assert
        Assert.Equal("users/svc-shared.md", result.RelativePath);
        Assert.Contains("# Shared service", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_is_deterministic_and_warns_when_two_files_claim_a_name()
    {
        // Arrange — both a-shared.md and b-shared.md match; a-shared sorts first
        WriteDoc("topics", "a-shared.md", "---\ntopics:\n  - orders.*.placed\n---\n# A");
        WriteDoc("topics", "b-shared.md", "---\ntopics:\n  - orders.*.placed\n---\n# B");
        var logger = new CapturingLogger<FileDocumentationStore>();
        var store = CreateStore(logger);

        // Act
        var result = store.Read(DocumentationKind.Topic, "orders.branch01.placed");

        // Assert — first (ordinal) file wins and an overlap Warning is logged
        Assert.Equal("topics/a-shared.md", result.RelativePath);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Read_indexes_a_file_with_no_front_matter_by_slug_only()
    {
        // Arrange
        WriteDoc("topics", "plain.md", "# No front matter");
        var store = CreateStore();

        // Act
        var bySlug = store.Read(DocumentationKind.Topic, "plain");
        var byPattern = store.Read(DocumentationKind.Topic, "anything.else");

        // Assert
        Assert.Equal("# No front matter", bySlug.Content);
        Assert.Null(byPattern.Content);
    }

    [Fact]
    public void Read_degrades_to_slug_only_and_warns_on_malformed_front_matter()
    {
        // Arrange — unclosed flow sequence is invalid YAML
        WriteDoc("topics", "broken.md", "---\ntopics: [a, b\n---\n# Broken");
        var logger = new CapturingLogger<FileDocumentationStore>();
        var store = CreateStore(logger);

        // Act — own slug still resolves; the dropped alias does not
        var bySlug = store.Read(DocumentationKind.Topic, "broken");
        var byAlias = store.Read(DocumentationKind.Topic, "a");

        // Assert — Read returns raw content (front matter included; the Web pipeline hides it at render)
        Assert.Equal("---\ntopics: [a, b\n---\n# Broken", bySlug.Content);
        Assert.Null(byAlias.Content);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void HasDocumentation_is_true_via_pattern_and_false_when_absent()
    {
        // Arrange
        WriteDoc("topics", "shared.md", "---\ntopics:\n  - orders.*.placed\n---\n# Shared");
        var store = CreateStore();

        // Act + Assert
        Assert.True(store.HasDocumentation(DocumentationKind.Topic, "orders.branch01.placed"));
        Assert.False(store.HasDocumentation(DocumentationKind.Topic, "unrelated.topic"));
    }

    [Fact]
    public void Rebuild_picks_up_a_newly_added_file()
    {
        // Arrange — first access builds an empty index
        var store = CreateStore();
        Assert.Null(store.Read(DocumentationKind.Topic, "late").Content);

        // Act — add a file, then rebuild
        WriteDoc("topics", "late.md", "# Late");
        var beforeRebuild = store.Read(DocumentationKind.Topic, "late").Content;
        store.Rebuild();
        var afterRebuild = store.Read(DocumentationKind.Topic, "late").Content;

        // Assert — cached index does not see it until Rebuild swaps in a fresh one
        Assert.Null(beforeRebuild);
        Assert.Equal("# Late", afterRebuild);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
