# Shared Topic/User Documentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let one markdown file document many branch-variant Kafka topics/users via optional YAML front-matter aliases (exact names or `*` globs), resolved through an immutable in-memory index.

**Architecture:** A pure Domain glob matcher (`DocumentationPattern`) plus an Infrastructure index (`DocumentationIndex`) built lazily and swapped atomically. `FileDocumentationStore` uses Markdig to locate front matter and YamlDotNet to deserialize alias lists; `Read`/`HasDocumentation` resolve a name to a file via the index (own-file slug wins, then first-matching pattern). Web's Markdig pipeline gains `UseYamlFrontMatter()` so the block never renders. Files are immutable in the container, so a one-time scan is safe.

**Tech Stack:** .NET 10, C#, Markdig 0.41.3, YamlDotNet 16.3.0, xUnit v3 + Microsoft.Testing.Platform, bUnit, NSubstitute, ArchUnitNET.

---

## ⚠️ Project conventions (read before every task)

- **No git actions.** Per `CLAUDE.md`, never run `git add`/`commit`/anything. The "frequent commits" rule from the writing-plans skill is **overridden**: each task ends with a **build + test verification** step instead of a commit. The user reviews and commits.
- **Treat warnings as errors.** CI builds with `-warnaserror` (Meziantou, SonarAnalyzer, Roslynator). Run `dotnet build --no-restore -warnaserror` before claiming a task done. Suppress narrowly with `#pragma warning disable <id>` + matching restore only when a rule genuinely doesn't apply.
- **Test runner is Microsoft.Testing.Platform**, not VSTest. Use the `dotnet test` filters shown (`--filter-class` / `--filter-method`).
- **Code style:** 4-space indent, `_camelCase` private fields, XML comments on all public members, primary constructors where possible, Arrange/Act/Assert comments in tests, snake_case test method names (no `Async` suffix).
- Run `dotnet restore` once at the start; afterwards use `--no-restore` for speed (except right after Task 2, which changes packages).

---

## File Structure

**New files:**
- `src/Kafdoc.Domain/Documentation/DocumentationPattern.cs` — pure `*`-glob matcher.
- `src/Kafdoc.Infrastructure/Documentation/DocumentationIndex.cs` — immutable index data holder (`BySlug` + `Patterns`).
- `test/Kafdoc.DomainTest/Documentation/DocumentationPatternTests.cs`
- (Infra tests are added into the existing `FileDocumentationStoreTests.cs`.)

**Modified files:**
- `Directory.Packages.props` — add `YamlDotNet` version.
- `src/Kafdoc.Infrastructure/Kafdoc.Infrastructure.csproj` — add `Markdig` + `YamlDotNet` package references.
- `src/Kafdoc.Domain/Documentation/IDocumentationStore.cs` — add `HasDocumentation`; later remove `ListSlugs`.
- `src/Kafdoc.Infrastructure/Documentation/FileDocumentationStore.cs` — index build + Markdig/YamlDotNet front matter + index-based `Read`/`HasDocumentation` + `Rebuild`.
- `src/Kafdoc.Application/Services/TopicQueryService.cs`, `UserQueryService.cs` — use `HasDocumentation`.
- `src/Kafdoc.Web/Program.cs` — add `.UseYamlFrontMatter()`.
- Tests in `Kafdoc.ApplicationTest`, `Kafdoc.WebTest`, `Kafdoc.InfrastructureTest`.

---

## Task 1: Pure glob matcher (`DocumentationPattern`)

**Files:**
- Create: `src/Kafdoc.Domain/Documentation/DocumentationPattern.cs`
- Test: `test/Kafdoc.DomainTest/Documentation/DocumentationPatternTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `test/Kafdoc.DomainTest/Documentation/DocumentationPatternTests.cs`:

```csharp
using Kafdoc.Domain.Documentation;

namespace Kafdoc.DomainTest.Documentation;

public class DocumentationPatternTests
{
    [Fact]
    public void Matches_star_spans_dots()
    {
        // Act + Assert
        Assert.True(DocumentationPattern.Matches("orders.*.placed", "orders.branch01.placed"));
        Assert.True(DocumentationPattern.Matches("orders.*.placed", "orders.branch07.placed"));
    }

    [Fact]
    public void Matches_exact_when_no_star()
    {
        // Act + Assert
        Assert.True(DocumentationPattern.Matches("legacy.orders.placed", "legacy.orders.placed"));
        Assert.False(DocumentationPattern.Matches("legacy.orders.placed", "legacy.orders.created"));
    }

    [Fact]
    public void Matches_is_case_sensitive_ordinal()
    {
        // Act + Assert
        Assert.False(DocumentationPattern.Matches("Orders.placed", "orders.placed"));
    }

    [Fact]
    public void Matches_leading_and_trailing_star()
    {
        // Act + Assert
        Assert.True(DocumentationPattern.Matches("*.placed", "orders.branch01.placed"));
        Assert.True(DocumentationPattern.Matches("orders.*", "orders.branch01.placed"));
    }

    [Fact]
    public void Matches_returns_false_on_mismatch()
    {
        // Act + Assert
        Assert.False(DocumentationPattern.Matches("orders.*.placed", "orders.branch01.created"));
    }

    [Fact]
    public void Matches_handles_empty_inputs()
    {
        // Act + Assert
        Assert.True(DocumentationPattern.Matches("", ""));
        Assert.True(DocumentationPattern.Matches("*", ""));
        Assert.False(DocumentationPattern.Matches("", "x"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --no-restore test/Kafdoc.DomainTest --filter-class "*DocumentationPatternTests*"`
Expected: FAIL — `DocumentationPattern` does not exist (compile error).

- [ ] **Step 3: Write the implementation**

Create `src/Kafdoc.Domain/Documentation/DocumentationPattern.cs`:

```csharp
namespace Kafdoc.Domain.Documentation;

/// <summary>Pure, dependency-free glob matching for documentation aliases.</summary>
public static class DocumentationPattern
{
    /// <summary>
    /// Ordinal, case-sensitive <c>*</c> glob match where <c>*</c> matches any run of
    /// characters (including dots). A pattern containing no <c>*</c> is an exact match.
    /// </summary>
    /// <param name="pattern">The glob or exact pattern.</param>
    /// <param name="name">The raw name to test.</param>
    /// <returns><c>true</c> when <paramref name="name"/> matches <paramref name="pattern"/>.</returns>
    public static bool Matches(string pattern, string name)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(name);

        var p = 0;
        var n = 0;
        var star = -1;
        var mark = 0;

        while (n < name.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                star = p;
                mark = n;
                p++;
            }
            else if (p < pattern.Length && pattern[p] == name[n])
            {
                p++;
                n++;
            }
            else if (star != -1)
            {
                p = star + 1;
                mark++;
                n = mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --no-restore test/Kafdoc.DomainTest --filter-class "*DocumentationPatternTests*"`
Expected: PASS (6 tests).

- [ ] **Step 5: Verify the build is clean**

Run: `dotnet build --no-restore -warnaserror src/Kafdoc.Domain`
Expected: Build succeeded, 0 warnings.

---

## Task 2: Add YamlDotNet + Markdig to Infrastructure packaging

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/Kafdoc.Infrastructure/Kafdoc.Infrastructure.csproj`

- [ ] **Step 1: Add the YamlDotNet version to central package management**

In `Directory.Packages.props`, add this line to the `<ItemGroup>` (keep the list alphabetical — place it after the `xunit.v3` line at the end, or anywhere in the group):

```xml
    <PackageVersion Include="YamlDotNet" Version="16.3.0" />
```

- [ ] **Step 2: Reference Markdig and YamlDotNet from Infrastructure**

In `src/Kafdoc.Infrastructure/Kafdoc.Infrastructure.csproj`, add two version-less `PackageReference` entries to the existing first `<ItemGroup>` (the one listing `Confluent.Kafka` etc.):

```xml
    <PackageReference Include="Markdig" />
    <PackageReference Include="YamlDotNet" />
```

The file's package `<ItemGroup>` becomes:

```xml
  <ItemGroup>
    <PackageReference Include="Confluent.Kafka" />
    <PackageReference Include="Markdig" />
    <PackageReference Include="Microsoft.Extensions.Configuration" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" />
    <PackageReference Include="YamlDotNet" />
  </ItemGroup>
```

- [ ] **Step 3: Restore and build to verify packages resolve**

Run: `dotnet restore && dotnet build --no-restore -warnaserror src/Kafdoc.Infrastructure`
Expected: Build succeeded, 0 warnings (no code uses the packages yet — this just proves they resolve).

---

## Task 3: The immutable index data holder (`DocumentationIndex`)

**Files:**
- Create: `src/Kafdoc.Infrastructure/Documentation/DocumentationIndex.cs`

This type is a pure data holder (no behavior), so it has no standalone test; it is exercised through `FileDocumentationStore` in Task 5.

- [ ] **Step 1: Create the index type**

Create `src/Kafdoc.Infrastructure/Documentation/DocumentationIndex.cs`:

```csharp
namespace Kafdoc.Infrastructure.Documentation;

/// <summary>
/// An immutable, content-free map from documentation slugs and front-matter glob
/// patterns to the relative file paths that satisfy them. Built once per kind and
/// swapped atomically.
/// </summary>
internal sealed class DocumentationIndex
{
    /// <summary>Creates the index.</summary>
    /// <param name="bySlug">Filename slug (no folder, no extension) to relative path, e.g. <c>orders.placed</c> → <c>topics/orders.placed.md</c>.</param>
    /// <param name="patterns">Ordered (glob, relative path) pairs gathered from front matter, sorted by relative path so resolution is a deterministic first-match.</param>
    public DocumentationIndex(
        IReadOnlyDictionary<string, string> bySlug,
        IReadOnlyList<(string Pattern, string RelativePath)> patterns)
    {
        BySlug = bySlug;
        Patterns = patterns;
    }

    /// <summary>Filename slug to relative path.</summary>
    public IReadOnlyDictionary<string, string> BySlug { get; }

    /// <summary>Ordered front-matter (glob, relative path) pairs; first match wins.</summary>
    public IReadOnlyList<(string Pattern, string RelativePath)> Patterns { get; }
}
```

- [ ] **Step 2: Verify the build is clean**

Run: `dotnet build --no-restore -warnaserror src/Kafdoc.Infrastructure`
Expected: Build succeeded, 0 warnings.

---

## Task 4: Add `HasDocumentation` to the port (additive)

We add `HasDocumentation` now and keep `ListSlugs` temporarily so every layer keeps compiling. `ListSlugs` is removed in Task 7 after Application is migrated.

**Files:**
- Modify: `src/Kafdoc.Domain/Documentation/IDocumentationStore.cs`

- [ ] **Step 1: Add the method to the interface**

In `src/Kafdoc.Domain/Documentation/IDocumentationStore.cs`, add the `HasDocumentation` member below `ListSlugs` (keep `ListSlugs` for now). The file becomes:

```csharp
namespace Kafdoc.Domain.Documentation;

/// <summary>Reads operator-authored markdown documentation for topics and users.</summary>
public interface IDocumentationStore
{
    /// <summary>Looks up documentation for a single entity.</summary>
    /// <param name="kind">Whether the name is a topic or a user.</param>
    /// <param name="name">The topic name or principal.</param>
    /// <returns>The lookup; <see cref="DocumentationLookup.Content"/> is <c>null</c> when no file exists.</returns>
    DocumentationLookup Read(DocumentationKind kind, string name);

    /// <summary>Lists the slugs that currently have a documentation file on disk.</summary>
    /// <param name="kind">Whether to list topic or user docs.</param>
    /// <returns>Slugs (file names without extension) present in the relevant folder.</returns>
    IReadOnlySet<string> ListSlugs(DocumentationKind kind);

    /// <summary>Indicates whether the name resolves to a documentation file (own slug or alias).</summary>
    /// <param name="kind">Whether the name is a topic or a user.</param>
    /// <param name="name">The topic name or principal.</param>
    /// <returns><c>true</c> when documentation exists; an index lookup with no content load.</returns>
    bool HasDocumentation(DocumentationKind kind, string name);
}
```

- [ ] **Step 2: Verify the build fails on the unimplemented member**

Run: `dotnet build --no-restore src/Kafdoc.Infrastructure`
Expected: FAIL — `FileDocumentationStore` does not implement `HasDocumentation`. (This is implemented in Task 5; this confirms the contract is in place.)

---

## Task 5: Index-based `FileDocumentationStore`

Replace the per-call directory scanning with a lazily-built per-kind index. Implement `Read` and `HasDocumentation` via index resolution, add `Rebuild`, and reimplement `ListSlugs` from the index (so existing Application code/tests stay green until Task 7).

**Files:**
- Modify: `src/Kafdoc.Infrastructure/Documentation/FileDocumentationStore.cs`
- Test: `test/Kafdoc.InfrastructureTest/Documentation/FileDocumentationStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

Replace the entire contents of `test/Kafdoc.InfrastructureTest/Documentation/FileDocumentationStoreTests.cs` with the following (this keeps the existing direct-match/missing/traversal tests and adds the new index/alias tests, plus a capturing logger to assert Warnings):

```csharp
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

        // Assert
        Assert.Equal("# Broken", bySlug.Content);
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
```

> Note: the old `ListSlugs_returns_file_names_without_extension` and `ListSlugs_returns_empty_when_the_directory_is_absent` tests are intentionally dropped here — `ListSlugs` is removed in Task 7 and is covered transitively by the index tests.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --no-restore test/Kafdoc.InfrastructureTest --filter-class "*FileDocumentationStoreTests*"`
Expected: FAIL — `HasDocumentation`/`Rebuild` not implemented and pattern resolution missing.

> These tests use a temp directory only (no Docker), so they run without a Kafka broker.

- [ ] **Step 3: Rewrite the store**

Replace the entire contents of `src/Kafdoc.Infrastructure/Documentation/FileDocumentationStore.cs` with:

```csharp
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

    /// <inheritdoc />
    public IReadOnlySet<string> ListSlugs(DocumentationKind kind) =>
        GetIndex(kind).BySlug.Keys.ToHashSet(StringComparer.Ordinal);

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

    private IReadOnlyList<string> ReadAliases(string file, DocumentationKind kind)
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

    private sealed class FrontMatter
    {
        public List<string>? Topics { get; set; }

        public List<string>? Users { get; set; }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --no-restore test/Kafdoc.InfrastructureTest --filter-class "*FileDocumentationStoreTests*"`
Expected: PASS (all tests in the class).

- [ ] **Step 5: Verify the build is clean (warnings as errors)**

Run: `dotnet build --no-restore -warnaserror src/Kafdoc.Infrastructure`
Expected: Build succeeded, 0 warnings.

> If the analyzer flags `_topicIndex ??= ...` on a volatile field (CS0420 is only for `ref`/`out`; `??=` does not pass by ref, so this should not fire). If any analyzer objects to the private nested `FrontMatter` class members lacking XML docs, note that private members do not require XML docs under this project's settings — no suppression should be needed. Only add a narrow `#pragma warning disable`/`restore` if a specific rule genuinely fires.

---

## Task 6: Application uses `HasDocumentation`

**Files:**
- Modify: `src/Kafdoc.Application/Services/TopicQueryService.cs`
- Modify: `src/Kafdoc.Application/Services/UserQueryService.cs`
- Test: `test/Kafdoc.ApplicationTest/Services/TopicQueryServiceTests.cs`
- Test: `test/Kafdoc.ApplicationTest/Services/UserQueryServiceTests.cs`

- [ ] **Step 1: Update the Application tests first (red)**

In `test/Kafdoc.ApplicationTest/Services/TopicQueryServiceTests.cs`:

Replace the `NoDocs()` helper body's `ListSlugs` line. The helper becomes:

```csharp
    private static IDocumentationStore NoDocs()
    {
        var docs = Substitute.For<IDocumentationStore>();
        docs.HasDocumentation(Arg.Any<DocumentationKind>(), Arg.Any<string>()).Returns(false);
        docs.Read(Arg.Any<DocumentationKind>(), Arg.Any<string>())
            .Returns(ci => new DocumentationLookup($"topics/{ci.ArgAt<string>(1)}.md", null));
        return docs;
    }
```

Replace the body of `GetTopics_sets_HasDocumentation_from_the_doc_store` so it drives the flag through `HasDocumentation`:

```csharp
    [Fact]
    public void GetTopics_sets_HasDocumentation_from_the_doc_store()
    {
        // Arrange
        var graph = new ClusterGraph(
            Topics: [new KafkaTopic("documented", 1), new KafkaTopic("bare", 1)],
            Users: [], ConsumerGroups: [], Producers: [], Consumers: [], UserGroups: [], GroupConsumption: []);
        var docs = Substitute.For<IDocumentationStore>();
        docs.HasDocumentation(DocumentationKind.Topic, Arg.Any<string>()).Returns(false);
        docs.HasDocumentation(DocumentationKind.Topic, "documented").Returns(true);
        var service = new TopicQueryService(StoreWith(graph), docs);

        // Act
        var topics = service.GetTopics();

        // Assert — ordered by name: "bare" then "documented"
        Assert.False(topics[0].HasDocumentation);
        Assert.True(topics[1].HasDocumentation);
    }
```

In `GetTopic_includes_documentation_content_and_path`, remove the now-invalid `docs.ListSlugs(...)` setup line:

```csharp
        docs.ListSlugs(Arg.Any<DocumentationKind>()).Returns(new HashSet<string>(StringComparer.Ordinal));
```

(delete that one line; the `Read` setup stays.)

In `test/Kafdoc.ApplicationTest/Services/UserQueryServiceTests.cs`, apply the symmetric changes:

`NoDocs()` becomes:

```csharp
    private static IDocumentationStore NoDocs()
    {
        var docs = Substitute.For<IDocumentationStore>();
        docs.HasDocumentation(Arg.Any<DocumentationKind>(), Arg.Any<string>()).Returns(false);
        docs.Read(Arg.Any<DocumentationKind>(), Arg.Any<string>())
            .Returns(ci => new DocumentationLookup($"users/{ci.ArgAt<string>(1)}.md", null));
        return docs;
    }
```

`GetUsers_sets_HasDocumentation_from_the_doc_store` becomes:

```csharp
    [Fact]
    public void GetUsers_sets_HasDocumentation_from_the_doc_store()
    {
        // Arrange
        var graph = new ClusterGraph(
            Topics: [],
            Users: [new KafkaUser("User:documented", false), new KafkaUser("User:bare", false)],
            ConsumerGroups: [], Producers: [], Consumers: [], UserGroups: [], GroupConsumption: []);
        var docs = Substitute.For<IDocumentationStore>();
        docs.HasDocumentation(DocumentationKind.User, Arg.Any<string>()).Returns(false);
        docs.HasDocumentation(DocumentationKind.User, "User:documented").Returns(true);
        var service = new UserQueryService(StoreWith(graph), docs);

        // Act
        var users = service.GetUsers();

        // Assert — ordered by principal: "User:bare" then "User:documented"
        Assert.False(users[0].HasDocumentation);
        Assert.True(users[1].HasDocumentation);
    }
```

In `GetUser_includes_documentation_content_and_path`, delete the line:

```csharp
        docs.ListSlugs(Arg.Any<DocumentationKind>()).Returns(new HashSet<string>(StringComparer.Ordinal));
```

- [ ] **Step 2: Run the Application tests to verify they fail**

Run: `dotnet test --no-restore test/Kafdoc.ApplicationTest --filter-class "*QueryServiceTests*"`
Expected: FAIL — the services still call `ListSlugs`, so `documented`/`bare` flags come back wrong (or compile-clean but assertions fail).

- [ ] **Step 3: Update `TopicQueryService.GetTopics`**

In `src/Kafdoc.Application/Services/TopicQueryService.cs`, delete this line from `GetTopics`:

```csharp
        var docSlugs = documentation.ListSlugs(DocumentationKind.Topic);
```

and change the final selector's documentation argument from:

```csharp
                docSlugs.Contains(DocumentationSlug.ForTopic(t.Name))))
```

to:

```csharp
                documentation.HasDocumentation(DocumentationKind.Topic, t.Name)))
```

- [ ] **Step 4: Update `UserQueryService.GetUsers`**

In `src/Kafdoc.Application/Services/UserQueryService.cs`, delete this line from `GetUsers`:

```csharp
        var docSlugs = documentation.ListSlugs(DocumentationKind.User);
```

and change the selector's documentation argument from:

```csharp
                docSlugs.Contains(DocumentationSlug.ForUser(u.Principal))))
```

to:

```csharp
                documentation.HasDocumentation(DocumentationKind.User, u.Principal)))
```

> `DocumentationSlug` is no longer referenced in these two files, but `using Kafdoc.Domain.Documentation;` is still needed for `DocumentationKind`/`IDocumentationStore` — leave the using directive in place.

- [ ] **Step 5: Run the Application tests to verify they pass**

Run: `dotnet test --no-restore test/Kafdoc.ApplicationTest --filter-class "*QueryServiceTests*"`
Expected: PASS.

- [ ] **Step 6: Verify the build is clean**

Run: `dotnet build --no-restore -warnaserror src/Kafdoc.Application`
Expected: Build succeeded, 0 warnings (no unused-using warning for `Kafdoc.Domain.Documentation`).

---

## Task 7: Remove the obsolete `ListSlugs` from the port

Now that nothing reads `ListSlugs`, delete it from the interface and the store.

**Files:**
- Modify: `src/Kafdoc.Domain/Documentation/IDocumentationStore.cs`
- Modify: `src/Kafdoc.Infrastructure/Documentation/FileDocumentationStore.cs`

- [ ] **Step 1: Remove `ListSlugs` from the interface**

In `src/Kafdoc.Domain/Documentation/IDocumentationStore.cs`, delete the `ListSlugs` member (the XML comment and the method line):

```csharp
    /// <summary>Lists the slugs that currently have a documentation file on disk.</summary>
    /// <param name="kind">Whether to list topic or user docs.</param>
    /// <returns>Slugs (file names without extension) present in the relevant folder.</returns>
    IReadOnlySet<string> ListSlugs(DocumentationKind kind);
```

- [ ] **Step 2: Remove `ListSlugs` from the store**

In `src/Kafdoc.Infrastructure/Documentation/FileDocumentationStore.cs`, delete the implementation:

```csharp
    /// <inheritdoc />
    public IReadOnlySet<string> ListSlugs(DocumentationKind kind) =>
        GetIndex(kind).BySlug.Keys.ToHashSet(StringComparer.Ordinal);
```

- [ ] **Step 3: Build the full solution to confirm nothing references it**

Run: `dotnet build --no-restore -warnaserror`
Expected: Build succeeded, 0 warnings. (If any test or file still references `ListSlugs`, the compiler will name it — search and remove that reference.)

- [ ] **Step 4: Run the affected test suites**

Run: `dotnet test --no-restore test/Kafdoc.DomainTest test/Kafdoc.ApplicationTest`
Expected: PASS.

> `test/Kafdoc.InfrastructureTest` is run later (Task 9) since its broker integration tests need Docker; the documentation tests there were already verified in Task 5.

---

## Task 8: Web renders without the front-matter block

**Files:**
- Modify: `src/Kafdoc.Web/Program.cs`
- Test: `test/Kafdoc.WebTest/MarkdownContentTests.cs`

- [ ] **Step 1: Add the failing Web test**

In `test/Kafdoc.WebTest/MarkdownContentTests.cs`, update the `RegisterPipeline` helper to include the front-matter extension:

```csharp
    private void RegisterPipeline() =>
        Services.AddSingleton(new MarkdownPipelineBuilder().UseAdvancedExtensions().UseYamlFrontMatter().DisableHtml().Build());
```

Then add this test method to the class:

```csharp
    [Fact]
    public void Does_not_render_the_yaml_front_matter_block()
    {
        // Arrange
        RegisterPipeline();
        const string markdown = "---\ntopics:\n  - orders.*.placed\n---\n# Orders placed\n";

        // Act
        var cut = Render<MarkdownContent>(ps => ps
            .Add(p => p.Markdown, markdown)
            .Add(p => p.Path, "topics/orders-shared.md"));

        // Assert — the front-matter keys are omitted; the heading renders
        Assert.DoesNotContain("orders.*.placed", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("<h1", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Orders placed", cut.Markup, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run the Web test to verify it fails**

Run: `dotnet test --no-restore test/Kafdoc.WebTest --filter-method "*Does_not_render_the_yaml_front_matter_block*"`
Expected: FAIL — without `UseYamlFrontMatter()` Markdig renders the `---` block as content (a horizontal rule / heading), so `orders.*.placed` appears in the markup.

> `Markdig.Extensions.Yaml.MarkdownPipelineBuilderExtensions.UseYamlFrontMatter()` is available via the `Markdig` package already referenced by `Kafdoc.Web`. The bUnit test resolves the extension method through the existing `using Markdig;` directive at the top of the test file.

- [ ] **Step 3: Add `.UseYamlFrontMatter()` to the production pipeline**

In `src/Kafdoc.Web/Program.cs`, change the pipeline registration from:

```csharp
builder.Services.AddSingleton(new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()
    .DisableHtml()
    .Build());
```

to:

```csharp
builder.Services.AddSingleton(new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()
    .UseYamlFrontMatter()
    .DisableHtml()
    .Build());
```

- [ ] **Step 4: Run the Web tests to verify they pass**

Run: `dotnet test --no-restore test/Kafdoc.WebTest --filter-class "*MarkdownContentTests*"`
Expected: PASS (existing 3 tests + the new one).

- [ ] **Step 5: Verify the Web build is clean**

Run: `dotnet build --no-restore -warnaserror src/Kafdoc.Web`
Expected: Build succeeded, 0 warnings.

---

## Task 9: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Restore and build the whole solution with warnings as errors**

Run: `dotnet restore && dotnet build --no-restore -warnaserror`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 2: Run all non-broker tests**

Run: `dotnet test --no-restore test/Kafdoc.DomainTest test/Kafdoc.ApplicationTest test/Kafdoc.WebTest test/Kafdoc.ArchitectureTest`
Expected: PASS. The ArchitectureTest suite confirms the port stays in Domain, the adapter in Infrastructure, and Web touches only DTOs/services — Markdig and YamlDotNet are third-party and unconstrained by the layer rules.

- [ ] **Step 3: Run the Infrastructure tests (needs Docker for the broker integration tests)**

Run: `dotnet test --no-restore test/Kafdoc.InfrastructureTest`
Expected: PASS. If no Docker daemon is reachable, at minimum confirm the documentation tests pass:
`dotnet test --no-restore test/Kafdoc.InfrastructureTest --filter-class "*FileDocumentationStoreTests*"`

- [ ] **Step 4: Hand off for review**

Per `CLAUDE.md`, do **not** perform any git actions. Report that all tasks are complete with the verification output, and let the user review and commit.

---

## Self-Review (performed against the spec)

- **§1 Authoring model** → Task 5 (`topics:`/`users:` keys via `FrontMatter`, filename-slug primary key, optional front matter, symmetric topics/users with `User:` stripping in `RawName`); §1 front-matter-never-rendered → Task 8.
- **§1 Glob semantics** (`*` spans dots, no-`*` exact, ordinal case-sensitive, matched against raw name) → Task 1 + `RawName` in Task 5.
- **§1 Precedence & conflicts** (own file wins, ordinal first-match, multi-file Warning) → `Resolve`/`BuildIndex` ordering in Task 5; tests `Read_prefers_the_entitys_own_file_over_a_matching_pattern`, `Read_is_deterministic_and_warns_when_two_files_claim_a_name`.
- **§2 In-memory index** (immutable, lazy, `volatile` swap, `Rebuild`, mappings only) → Task 3 + `GetIndex`/`Rebuild` in Task 5; test `Rebuild_picks_up_a_newly_added_file`.
- **§2 Resolution** (slug → first pattern → none) → `Resolve` in Task 5.
- **§3 Domain port** (`Read` + `HasDocumentation`, `ListSlugs` replaced, `DocumentationLookup` unchanged, RelativePath = shared file on match / own path on miss) → Tasks 4, 6, 7; `Read` return logic in Task 5.
- **§4 Pure matching helper** → Task 1.
- **§5 Infrastructure adapter** (Markdig locates block, YamlDotNet deserializes, raw content returned, traversal guard, narrow I/O handling, malformed → Warning + slug-only) → Task 5.
- **§6 Application** (per-row `HasDocumentation`, `GetTopic`/`GetUser` unchanged, DTOs unchanged) → Task 6.
- **§6a Web** (`UseYamlFrontMatter()` in `Program.cs`, bUnit pipeline updated) → Task 8.
- **§7 Error handling** → covered across Tasks 5 (malformed/IO/traversal/overlap) and the missing-file path.
- **§8 Testing** (Domain, Infrastructure, Application, Web, Architecture) → Tasks 1, 5, 6, 8, 9.
- **Packaging** (YamlDotNet in `Directory.Packages.props`, Markdig + YamlDotNet refs in Infrastructure csproj) → Task 2.

No placeholders remain; all method/property names (`HasDocumentation`, `Rebuild`, `DocumentationPattern.Matches`, `DocumentationIndex.BySlug`/`Patterns`, `FrontMatter.Topics`/`Users`, `RawName`, `Resolve`, `BuildIndex`, `ReadAliases`) are consistent across tasks.
```