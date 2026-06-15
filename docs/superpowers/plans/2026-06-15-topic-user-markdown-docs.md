# Topic & User Markdown Documentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let operators attach Markdown documentation to individual Kafka topics and users by dropping `.md` files into the deployment; detail pages render the file (or show the expected filename when absent) and overview pages flag which entities already have docs.

**Architecture:** A documentation **port** (`IDocumentationStore`) lives in Domain with a pure name→slug helper; a **file adapter** in Infrastructure reads files from disk; the existing query services fold doc data into their DTOs; the Web layer owns Markdown→HTML rendering via a `MarkdownContent` component (Markdig). Mirrors the existing `IKafkaClusterReader` port/adapter pattern. Files are read fresh on every page view (no cache).

**Tech Stack:** .NET 10, C#, Blazor Server, Markdig (markdown rendering), xUnit v3, bUnit, NSubstitute, central package management.

> **Git note:** This repo's CLAUDE.md says *"Do not perform any git actions! I will review the code and perform git actions myself."* So this plan has **no commit steps**. Each task ends with a build+test verification checkpoint; the user reviews and commits between tasks.

> **Build-scope note:** Because adding positional fields to the DTO records changes their constructors, the *whole solution* compiles green only after Task 5. Tasks 1–4 verify against the specific projects they touch (e.g. `dotnet build src/Kafdoc.Application` + its test project). This is expected and called out per task.

---

### Task 1: Domain documentation port + slug helper

**Files:**
- Create: `src/Kafdoc.Domain/Documentation/DocumentationKind.cs`
- Create: `src/Kafdoc.Domain/Documentation/DocumentationLookup.cs`
- Create: `src/Kafdoc.Domain/Documentation/IDocumentationStore.cs`
- Create: `src/Kafdoc.Domain/Documentation/DocumentationSlug.cs`
- Test: `test/Kafdoc.DomainTest/Documentation/DocumentationSlugTests.cs`

- [ ] **Step 1: Write the failing test**

Create `test/Kafdoc.DomainTest/Documentation/DocumentationSlugTests.cs`:

```csharp
using Kafdoc.Domain.Documentation;

namespace Kafdoc.DomainTest.Documentation;

public class DocumentationSlugTests
{
    [Fact]
    public void ForTopic_keeps_dots_in_the_name()
    {
        // Act
        var slug = DocumentationSlug.ForTopic("orders.placed");

        // Assert
        Assert.Equal("orders.placed", slug);
    }

    [Fact]
    public void ForUser_strips_the_user_prefix()
    {
        // Act
        var slug = DocumentationSlug.ForUser("User:svc-payments");

        // Assert
        Assert.Equal("svc-payments", slug);
    }

    [Fact]
    public void ForUser_without_prefix_uses_the_whole_name()
    {
        // Act
        var slug = DocumentationSlug.ForUser("svc-payments");

        // Assert
        Assert.Equal("svc-payments", slug);
    }

    [Fact]
    public void ForTopic_replaces_filesystem_illegal_characters_with_underscore()
    {
        // Act — colon, slash, backslash, pipe are illegal
        var slug = DocumentationSlug.ForTopic("a:b/c\\d|e");

        // Assert
        Assert.Equal("a_b_c_d_e", slug);
    }

    [Fact]
    public void ForTopic_strips_leading_dots_to_neutralize_traversal()
    {
        // Act
        var slug = DocumentationSlug.ForTopic("../etc/passwd");

        // Assert — slashes become underscores and leading dots are removed
        Assert.Equal("etc_passwd", slug);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --no-restore test/Kafdoc.DomainTest --filter-class "*DocumentationSlugTests*"`
Expected: FAIL — `DocumentationSlug` does not exist (compile error).

- [ ] **Step 3: Create the slug helper**

Create `src/Kafdoc.Domain/Documentation/DocumentationSlug.cs`:

```csharp
namespace Kafdoc.Domain.Documentation;

/// <summary>Maps topic names and principals to documentation file slugs (no folder, no extension).</summary>
public static class DocumentationSlug
{
    private static readonly char[] IllegalChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>The slug for a topic.</summary>
    /// <param name="topicName">The topic name.</param>
    /// <returns>The file slug.</returns>
    public static string ForTopic(string topicName) => Slug(topicName);

    /// <summary>The slug for a principal; the <c>User:</c> prefix is stripped first.</summary>
    /// <param name="principal">The principal, e.g. <c>User:svc-payments</c>.</param>
    /// <returns>The file slug.</returns>
    public static string ForUser(string principal)
    {
        const string prefix = "User:";
        var name = principal.StartsWith(prefix, StringComparison.Ordinal)
            ? principal[prefix.Length..]
            : principal;
        return Slug(name);
    }

    private static string Slug(string value)
    {
        var chars = new char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            chars[i] = Array.IndexOf(IllegalChars, value[i]) >= 0 ? '_' : value[i];
        }

        return new string(chars).TrimStart('.');
    }
}
```

- [ ] **Step 4: Create the port types**

Create `src/Kafdoc.Domain/Documentation/DocumentationKind.cs`:

```csharp
namespace Kafdoc.Domain.Documentation;

/// <summary>The kind of entity a documentation file describes.</summary>
public enum DocumentationKind
{
    /// <summary>A Kafka topic.</summary>
    Topic,

    /// <summary>A Kafka user (principal).</summary>
    User,
}
```

Create `src/Kafdoc.Domain/Documentation/DocumentationLookup.cs`:

```csharp
namespace Kafdoc.Domain.Documentation;

/// <summary>The result of a documentation lookup.</summary>
/// <param name="RelativePath">The expected file path relative to the docs root, always populated (e.g. <c>topics/orders.placed.md</c>).</param>
/// <param name="Content">The raw markdown, or <c>null</c> when no file exists.</param>
public sealed record DocumentationLookup(string RelativePath, string? Content);
```

Create `src/Kafdoc.Domain/Documentation/IDocumentationStore.cs`:

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
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --no-restore test/Kafdoc.DomainTest --filter-class "*DocumentationSlugTests*"`
Expected: PASS (5 tests).

- [ ] **Step 6: Verify the Domain build is warning-clean**

Run: `dotnet build --no-restore -warnaserror src/Kafdoc.Domain`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 7: Checkpoint** — pause for the user to review/commit.

---

### Task 2: Infrastructure file adapter + options + DI + config

**Files:**
- Modify: `src/Kafdoc.Infrastructure/Kafdoc.Infrastructure.csproj` (add Hosting.Abstractions reference)
- Create: `src/Kafdoc.Infrastructure/Documentation/DocumentationOptions.cs`
- Create: `src/Kafdoc.Infrastructure/Documentation/FileDocumentationStore.cs`
- Modify: `src/Kafdoc.Infrastructure/Configuration.cs`
- Modify: `src/Kafdoc.Web/appsettings.json`
- Test: `test/Kafdoc.InfrastructureTest/Documentation/FileDocumentationStoreTests.cs`

- [ ] **Step 1: Add the Hosting.Abstractions package reference**

In `src/Kafdoc.Infrastructure/Kafdoc.Infrastructure.csproj`, add to the first `<ItemGroup>` (the version is already pinned in `Directory.Packages.props`; this also brings `ILogger`/`IOptions` transitively):

```xml
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
```

- [ ] **Step 2: Write the failing test**

Create `test/Kafdoc.InfrastructureTest/Documentation/FileDocumentationStoreTests.cs` (a plain unit test — no Kafka container needed):

```csharp
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
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --no-restore test/Kafdoc.InfrastructureTest --filter-class "*FileDocumentationStoreTests*"`
Expected: FAIL — `DocumentationOptions`/`FileDocumentationStore` do not exist (compile error).

- [ ] **Step 4: Create the options**

Create `src/Kafdoc.Infrastructure/Documentation/DocumentationOptions.cs`:

```csharp
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
```

- [ ] **Step 5: Create the file adapter**

Create `src/Kafdoc.Infrastructure/Documentation/FileDocumentationStore.cs`:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Kafdoc.Domain.Documentation;

namespace Kafdoc.Infrastructure.Documentation;

/// <summary>Reads markdown documentation files from disk on demand.</summary>
public sealed class FileDocumentationStore : IDocumentationStore
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

        DocumentationLookup Absent(Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read documentation file {Path}", fullPath);
            return new DocumentationLookup(relativePath, null);
        }

        try
        {
            var content = File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
            return new DocumentationLookup(relativePath, content);
        }
        catch (IOException ex)
        {
            return Absent(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Absent(ex);
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
            _logger.LogWarning(ex, "Failed to list documentation in {Directory}", directory);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Failed to list documentation in {Directory}", directory);
        }

        return result;
    }

    private static string Folder(DocumentationKind kind) => kind == DocumentationKind.Topic ? "topics" : "users";

    private static string Slug(DocumentationKind kind, string name) =>
        kind == DocumentationKind.Topic ? DocumentationSlug.ForTopic(name) : DocumentationSlug.ForUser(name);
}
```

- [ ] **Step 6: Register in DI**

In `src/Kafdoc.Infrastructure/Configuration.cs`, add `using Kafdoc.Infrastructure.Documentation;` to the usings, then add these two lines inside `ConfigureInfrastructure`, just before `services.AddSingleton<IKafkaClusterReader, ConfluentKafkaClusterReader>();`:

```csharp
        services.AddOptions<DocumentationOptions>()
            .Bind(configuration.GetSection(DocumentationOptions.SectionName));
        services.AddSingleton<IDocumentationStore, FileDocumentationStore>();
```

(`IDocumentationStore` resolves via `using Kafdoc.Domain.Kafka;` already present plus a new `using Kafdoc.Domain.Documentation;` — add that using too.)

- [ ] **Step 7: Add the config section**

In `src/Kafdoc.Web/appsettings.json`, add a top-level `Documentation` section (e.g. after the `Kafka` block — mind the trailing comma on the preceding `}`):

```json
  "Documentation": {
    "RootPath": ""
  }
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test --no-restore test/Kafdoc.InfrastructureTest --filter-class "*FileDocumentationStoreTests*"`
Expected: PASS (6 tests). No Docker required for this class.

- [ ] **Step 9: Verify the Infrastructure build is warning-clean**

Run: `dotnet build --no-restore -warnaserror src/Kafdoc.Infrastructure`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 10: Checkpoint** — pause for the user to review/commit.

---

### Task 3: Application — DTO fields + query-service wiring

**Files:**
- Modify: `src/Kafdoc.Application/Dtos/TopicDetailDto.cs`
- Modify: `src/Kafdoc.Application/Dtos/UserDetailDto.cs`
- Modify: `src/Kafdoc.Application/Dtos/TopicSummaryDto.cs`
- Modify: `src/Kafdoc.Application/Dtos/UserSummaryDto.cs`
- Modify: `src/Kafdoc.Application/Services/TopicQueryService.cs`
- Modify: `src/Kafdoc.Application/Services/UserQueryService.cs`
- Test: `test/Kafdoc.ApplicationTest/Services/TopicQueryServiceTests.cs` (modify)
- Test: `test/Kafdoc.ApplicationTest/Services/UserQueryServiceTests.cs` (modify)

- [ ] **Step 1: Extend the DTOs**

In `src/Kafdoc.Application/Dtos/TopicDetailDto.cs`, replace the record with (adds two trailing params + their doc comments):

```csharp
public sealed record TopicDetailDto(
    string Name,
    int PartitionCount,
    IReadOnlyList<string> Producers,
    IReadOnlyList<TopicConsumerDto> ConsumerGroups,
    IReadOnlyList<string> ReadOnlyPrincipals,
    string DocumentationPath,
    string? Documentation);
```

Add to the XML doc comment block above it:

```csharp
/// <param name="DocumentationPath">The expected documentation file path, always set (e.g. <c>topics/orders.placed.md</c>).</param>
/// <param name="Documentation">The raw markdown for the topic, or <c>null</c> if no file exists.</param>
```

In `src/Kafdoc.Application/Dtos/UserDetailDto.cs`, replace the record with:

```csharp
public sealed record UserDetailDto(
    string Principal,
    bool HasScramCredentials,
    IReadOnlyList<string> ProducesTopics,
    IReadOnlyList<string> ConsumesTopics,
    IReadOnlyList<string> Groups,
    string DocumentationPath,
    string? Documentation);
```

Add to its XML doc block:

```csharp
/// <param name="DocumentationPath">The expected documentation file path, always set (e.g. <c>users/svc-payments.md</c>).</param>
/// <param name="Documentation">The raw markdown for the user, or <c>null</c> if no file exists.</param>
```

In `src/Kafdoc.Application/Dtos/TopicSummaryDto.cs`, replace the record line with:

```csharp
public sealed record TopicSummaryDto(string Name, int PartitionCount, int ProducerCount, int ConsumerGroupCount, bool HasDocumentation);
```

Add to its XML doc block:

```csharp
/// <param name="HasDocumentation">Whether a documentation file exists for the topic.</param>
```

In `src/Kafdoc.Application/Dtos/UserSummaryDto.cs`, replace the record line with:

```csharp
public sealed record UserSummaryDto(string Principal, bool HasScramCredentials, int ProducesCount, int ConsumesCount, bool HasDocumentation);
```

Add to its XML doc block:

```csharp
/// <param name="HasDocumentation">Whether a documentation file exists for the user.</param>
```

- [ ] **Step 2: Wire `TopicQueryService` to the doc store**

In `src/Kafdoc.Application/Services/TopicQueryService.cs`:

Add `using Kafdoc.Domain.Documentation;` to the usings. Change the class declaration to take the store:

```csharp
internal sealed class TopicQueryService(ISnapshotStore store, IDocumentationStore documentation) : ITopicQueryService
```

In `GetTopics`, immediately before the final `return graph.Topics` projection, add:

```csharp
        var docSlugs = documentation.ListSlugs(DocumentationKind.Topic);
```

and change the `.Select(...)` projection to set the new flag (append the new argument):

```csharp
            .Select(t => new TopicSummaryDto(
                t.Name,
                t.PartitionCount,
                producersByTopic.GetValueOrDefault(t.Name),
                groupsByTopic.GetValueOrDefault(t.Name),
                docSlugs.Contains(DocumentationSlug.ForTopic(t.Name))))
```

In `GetTopic`, change the final `return` to include the lookup:

```csharp
        var doc = documentation.Read(DocumentationKind.Topic, name);
        return new TopicDetailDto(topic.Name, topic.PartitionCount, producers, consumerGroups, readOnlyPrincipals, doc.RelativePath, doc.Content);
```

- [ ] **Step 3: Wire `UserQueryService` to the doc store**

In `src/Kafdoc.Application/Services/UserQueryService.cs`:

Add `using Kafdoc.Domain.Documentation;`. Change the class declaration:

```csharp
internal sealed class UserQueryService(ISnapshotStore store, IDocumentationStore documentation) : IUserQueryService
```

In `GetUsers`, before the `return graph.Users` projection, add:

```csharp
        var docSlugs = documentation.ListSlugs(DocumentationKind.User);
```

and append the flag to the `UserSummaryDto` projection:

```csharp
            .Select(u => new UserSummaryDto(
                u.Principal,
                u.HasScramCredentials,
                produces.GetValueOrDefault(u.Principal),
                consumes.GetValueOrDefault(u.Principal),
                docSlugs.Contains(DocumentationSlug.ForUser(u.Principal))))
```

In `GetUser`, change the final `return` to:

```csharp
        var doc = documentation.Read(DocumentationKind.User, principal);
        return new UserDetailDto(
            user.Principal,
            user.HasScramCredentials,
            DistinctSorted(graph.Producers.Where(p => Eq(p.Principal, principal)).Select(p => p.Topic)),
            DistinctSorted(graph.Consumers.Where(c => Eq(c.Principal, principal)).Select(c => c.Topic)),
            DistinctSorted(graph.UserGroups.Where(g => Eq(g.Principal, principal)).Select(g => g.GroupId)),
            doc.RelativePath,
            doc.Content);
```

- [ ] **Step 4: Update existing tests to pass a doc store, and add new doc tests (UserQueryServiceTests)**

In `test/Kafdoc.ApplicationTest/Services/UserQueryServiceTests.cs`:

Add usings at the top:

```csharp
using NSubstitute;

using Kafdoc.Domain.Documentation;
```

Add a helper inside the class (near `StoreWith`):

```csharp
    private static IDocumentationStore NoDocs()
    {
        var docs = Substitute.For<IDocumentationStore>();
        docs.ListSlugs(Arg.Any<DocumentationKind>()).Returns(new HashSet<string>(StringComparer.Ordinal));
        docs.Read(Arg.Any<DocumentationKind>(), Arg.Any<string>())
            .Returns(ci => new DocumentationLookup($"users/{ci.ArgAt<string>(1)}.md", null));
        return docs;
    }
```

Update the three existing `new UserQueryService(...)` constructions to pass `NoDocs()`:
- `new UserQueryService(new SnapshotStore())` → `new UserQueryService(new SnapshotStore(), NoDocs())`
- `new UserQueryService(StoreWith(graph))` → `new UserQueryService(StoreWith(graph), NoDocs())` (two occurrences)
- `new UserQueryService(StoreWith(new ClusterGraph([], [], [], [], [], [], [])))` → add `, NoDocs()` before the closing paren.

Then add two new tests:

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
        docs.ListSlugs(DocumentationKind.User).Returns(new HashSet<string>(StringComparer.Ordinal) { "documented" });
        var service = new UserQueryService(StoreWith(graph), docs);

        // Act
        var users = service.GetUsers();

        // Assert — ordered by principal: "User:bare" then "User:documented"
        Assert.False(users[0].HasDocumentation);
        Assert.True(users[1].HasDocumentation);
    }

    [Fact]
    public void GetUser_includes_documentation_content_and_path()
    {
        // Arrange
        var graph = new ClusterGraph(
            Topics: [], Users: [new KafkaUser("User:c", false)],
            ConsumerGroups: [], Producers: [], Consumers: [], UserGroups: [], GroupConsumption: []);
        var docs = Substitute.For<IDocumentationStore>();
        docs.ListSlugs(Arg.Any<DocumentationKind>()).Returns(new HashSet<string>(StringComparer.Ordinal));
        docs.Read(DocumentationKind.User, "User:c").Returns(new DocumentationLookup("users/c.md", "# hello"));
        var service = new UserQueryService(StoreWith(graph), docs);

        // Act
        var detail = service.GetUser("User:c");

        // Assert
        Assert.NotNull(detail);
        Assert.Equal("users/c.md", detail!.DocumentationPath);
        Assert.Equal("# hello", detail.Documentation);
    }
```

- [ ] **Step 5: Update existing tests + add new doc tests (TopicQueryServiceTests)**

In `test/Kafdoc.ApplicationTest/Services/TopicQueryServiceTests.cs`:

Add usings:

```csharp
using NSubstitute;

using Kafdoc.Domain.Documentation;
```

Add a `NoDocs()` helper (same shape, but topics path):

```csharp
    private static IDocumentationStore NoDocs()
    {
        var docs = Substitute.For<IDocumentationStore>();
        docs.ListSlugs(Arg.Any<DocumentationKind>()).Returns(new HashSet<string>(StringComparer.Ordinal));
        docs.Read(Arg.Any<DocumentationKind>(), Arg.Any<string>())
            .Returns(ci => new DocumentationLookup($"topics/{ci.ArgAt<string>(1)}.md", null));
        return docs;
    }
```

Update every existing `new TopicQueryService(...)` construction in the file to append `, NoDocs()` as the second constructor argument. There are four:
- `new TopicQueryService(new SnapshotStore())` → `new TopicQueryService(new SnapshotStore(), NoDocs())`
- `new TopicQueryService(StoreWith(graph))` → `new TopicQueryService(StoreWith(graph), NoDocs())` (two occurrences)
- `new TopicQueryService(StoreWith(new ClusterGraph([], [], [], [], [], [], [])))` → add `, NoDocs()` before the final closing paren.

Then add two new tests (this file's other tests use the full 7-arg positional `ClusterGraph`, matched below):

```csharp
    [Fact]
    public void GetTopics_sets_HasDocumentation_from_the_doc_store()
    {
        // Arrange
        var graph = new ClusterGraph(
            Topics: [new KafkaTopic("documented", 1), new KafkaTopic("bare", 1)],
            Users: [], ConsumerGroups: [], Producers: [], Consumers: [], UserGroups: [], GroupConsumption: []);
        var docs = Substitute.For<IDocumentationStore>();
        docs.ListSlugs(DocumentationKind.Topic).Returns(new HashSet<string>(StringComparer.Ordinal) { "documented" });
        var service = new TopicQueryService(StoreWith(graph), docs);

        // Act
        var topics = service.GetTopics();

        // Assert — ordered by name: "bare" then "documented"
        Assert.False(topics[0].HasDocumentation);
        Assert.True(topics[1].HasDocumentation);
    }

    [Fact]
    public void GetTopic_includes_documentation_content_and_path()
    {
        // Arrange
        var graph = new ClusterGraph(
            Topics: [new KafkaTopic("orders.placed", 1)],
            Users: [], ConsumerGroups: [], Producers: [], Consumers: [], UserGroups: [], GroupConsumption: []);
        var docs = Substitute.For<IDocumentationStore>();
        docs.ListSlugs(Arg.Any<DocumentationKind>()).Returns(new HashSet<string>(StringComparer.Ordinal));
        docs.Read(DocumentationKind.Topic, "orders.placed").Returns(new DocumentationLookup("topics/orders.placed.md", "# Orders"));
        var service = new TopicQueryService(StoreWith(graph), docs);

        // Act
        var detail = service.GetTopic("orders.placed");

        // Assert
        Assert.NotNull(detail);
        Assert.Equal("topics/orders.placed.md", detail!.DocumentationPath);
        Assert.Equal("# Orders", detail.Documentation);
    }
```

> Note: if `TopicQueryServiceTests` does not already construct graphs with the full 7-arg `ClusterGraph`, mirror the existing tests' style in that file. The two new tests above use named/positional args consistent with `UserQueryServiceTests`.

- [ ] **Step 6: Run the Application tests**

Run: `dotnet test --no-restore test/Kafdoc.ApplicationTest`
Expected: PASS (all existing tests plus the 4 new ones).

- [ ] **Step 7: Verify the Application build is warning-clean**

Run: `dotnet build --no-restore -warnaserror src/Kafdoc.Application`
Expected: Build succeeded, 0 warnings. (The Web project will NOT build yet — that's fixed in Task 5.)

- [ ] **Step 8: Checkpoint** — pause for the user to review/commit.

---

### Task 4: Web — Markdig package + `MarkdownContent` component

**Files:**
- Modify: `Directory.Packages.props` (pin Markdig)
- Modify: `src/Kafdoc.Web/Kafdoc.Web.csproj` (reference Markdig)
- Modify: `src/Kafdoc.Web/Program.cs` (register the pipeline)
- Modify: `src/Kafdoc.Web/Components/_Imports.razor` (using for the Shared namespace)
- Create: `src/Kafdoc.Web/Components/Shared/MarkdownContent.razor`
- Test: `test/Kafdoc.WebTest/MarkdownContentTests.cs`

- [ ] **Step 1: Pin the Markdig package**

In `Directory.Packages.props`, add inside the `<ItemGroup>` (keep the list alphabetical — place after `Confluent.Kafka`):

```xml
    <PackageVersion Include="Markdig" Version="0.41.3" />
```

> If restore reports that version is unavailable, run `dotnet package search Markdig --take 1 --prerelease false` and use the latest stable version it reports.

- [ ] **Step 2: Reference Markdig in the Web project**

In `src/Kafdoc.Web/Kafdoc.Web.csproj`, add a new `<ItemGroup>` (the SDK-Web project currently has only a ProjectReference group):

```xml
  <ItemGroup>
    <PackageReference Include="Markdig" />
  </ItemGroup>
```

- [ ] **Step 3: Register the Markdig pipeline singleton**

In `src/Kafdoc.Web/Program.cs`, add `using Markdig;` at the top with the other usings, then after the `builder.Services.ConfigureApplication(builder.Configuration);` line add:

```csharp
builder.Services.AddSingleton(new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()
    .DisableHtml()
    .Build());
```

- [ ] **Step 4: Make the Shared namespace available to components**

In `src/Kafdoc.Web/Components/_Imports.razor`, add:

```razor
@using Kafdoc.Web.Components.Shared
```

- [ ] **Step 5: Write the failing test**

Create `test/Kafdoc.WebTest/MarkdownContentTests.cs`:

```csharp
using Bunit;

using Markdig;

using Microsoft.Extensions.DependencyInjection;

using Kafdoc.Web.Components.Shared;

namespace Kafdoc.WebTest;

public sealed class MarkdownContentTests : Bunit.BunitContext
{
    private void RegisterPipeline() =>
        Services.AddSingleton(new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build());

    [Fact]
    public void Renders_markdown_as_html_with_a_source_caption()
    {
        // Arrange
        RegisterPipeline();

        // Act
        var cut = Render<MarkdownContent>(ps => ps
            .Add(p => p.Markdown, "# Title")
            .Add(p => p.Path, "topics/orders.md"));

        // Assert
        Assert.Contains("<h1", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Source:", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("topics/orders.md", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Shows_filename_hint_when_no_markdown_is_present()
    {
        // Arrange
        RegisterPipeline();

        // Act
        var cut = Render<MarkdownContent>(ps => ps
            .Add(p => p.Markdown, (string?)null)
            .Add(p => p.Path, "users/svc-payments.md"));

        // Assert
        Assert.Contains("No additional information available", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("users/svc-payments.md", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Escapes_raw_html_in_markdown()
    {
        // Arrange
        RegisterPipeline();

        // Act
        var cut = Render<MarkdownContent>(ps => ps
            .Add(p => p.Markdown, "<script>alert(1)</script>")
            .Add(p => p.Path, "topics/x.md"));

        // Assert — DisableHtml escapes the tag instead of emitting it
        Assert.DoesNotContain("<script>", cut.Markup, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test --no-restore test/Kafdoc.WebTest --filter-class "*MarkdownContentTests*"`
Expected: FAIL — `MarkdownContent` does not exist (compile error).

- [ ] **Step 7: Create the component**

Create `src/Kafdoc.Web/Components/Shared/MarkdownContent.razor`:

```razor
@inject Markdig.MarkdownPipeline Pipeline

<div class="documentation">
    @if (Markdown is null)
    {
        <p><em>No additional information available.</em></p>
        <p class="doc-source">Create <code>@Path</code> to add documentation.</p>
    }
    else
    {
        <div class="doc-body">@((MarkupString)Markdig.Markdown.ToHtml(Markdown, Pipeline))</div>
        <p class="doc-source">Source: <code>@Path</code></p>
    }
</div>

@code {
    /// <summary>The raw markdown to render, or <c>null</c> when no file exists.</summary>
    [Parameter]
    public string? Markdown { get; set; }

    /// <summary>The expected file path, always shown so authors know what to name a new file.</summary>
    [Parameter]
    [EditorRequired]
    public string Path { get; set; } = string.Empty;
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test --no-restore test/Kafdoc.WebTest --filter-class "*MarkdownContentTests*"`
Expected: PASS (3 tests).

- [ ] **Step 9: Checkpoint** — pause for the user to review/commit.

---

### Task 5: Web — wire detail pages, overview columns, fix existing tests

**Files:**
- Modify: `src/Kafdoc.Web/Components/Pages/TopicDetail.razor`
- Modify: `src/Kafdoc.Web/Components/Pages/UserDetail.razor`
- Modify: `src/Kafdoc.Web/Components/Pages/Topics.razor`
- Modify: `src/Kafdoc.Web/Components/Pages/Users.razor`
- Test: `test/Kafdoc.WebTest/UserDetailPageTests.cs` (modify)
- Test: `test/Kafdoc.WebTest/UsersPageTests.cs` (modify)
- Test: `test/Kafdoc.WebTest/TopicsPageTests.cs` (modify)

- [ ] **Step 1: Add the documentation section to `TopicDetail.razor`**

In `src/Kafdoc.Web/Components/Pages/TopicDetail.razor`, inside the `else` block, after the read-only-principals block and before the closing `}` of the `else`, add:

```razor
    <h2>Documentation</h2>
    <MarkdownContent Markdown="@detail.Documentation" Path="@detail.DocumentationPath" />
```

- [ ] **Step 2: Add the documentation section to `UserDetail.razor`**

In `src/Kafdoc.Web/Components/Pages/UserDetail.razor`, inside the `else` block, after the "Consumes" section and before the closing `}`, add:

```razor
    <h2>Documentation</h2>
    <MarkdownContent Markdown="@detail.Documentation" Path="@detail.DocumentationPath" />
```

- [ ] **Step 3: Add the "Docs" column to `Topics.razor`**

In `src/Kafdoc.Web/Components/Pages/Topics.razor`, add a header cell after `<th>Consumer groups</th>`:

```razor
                <th>Docs</th>
```

and a body cell after the `ConsumerGroupCount` `<td>`:

```razor
                    <td>@(topic.HasDocumentation ? "✓" : "")</td>
```

- [ ] **Step 4: Add the "Docs" column to `Users.razor`**

In `src/Kafdoc.Web/Components/Pages/Users.razor`, add a header cell after `<th>Consumes</th>`:

```razor
<th>Docs</th>
```

and a body cell after the `ConsumesCount` `<td>`:

```razor
                    <td>@(u.HasDocumentation ? "✓" : "")</td>
```

- [ ] **Step 5: Fix `UserDetailPageTests` (new DTO args + pipeline registration)**

In `test/Kafdoc.WebTest/UserDetailPageTests.cs`:

Add usings:

```csharp
using Markdig;
```

Register the Markdig pipeline so `UserDetail` (which now renders `MarkdownContent`) can resolve it. Add this private helper to the class and call it at the start of each test's Arrange:

```csharp
    private void RegisterPipeline() =>
        Services.AddSingleton(new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build());
```

Add `RegisterPipeline();` as the first line of each test body.

Update the two `new UserDetailDto(...)` constructions to add the new trailing args:
- For the "known principal" test (`User:alice`), append:

```csharp
            DocumentationPath: "users/alice.md",
            Documentation: "# Alice service");
```

  (i.e. the object becomes `new UserDetailDto(Principal: "User:alice", HasScramCredentials: true, ProducesTopics: ["orders", "payments"], ConsumesTopics: ["shipments"], Groups: ["billing-svc"], DocumentationPath: "users/alice.md", Documentation: "# Alice service")`.)

- For the "no produce topics" test (`User:reader`), append:

```csharp
            DocumentationPath: "users/reader.md",
            Documentation: null);
```

Add one new test asserting the doc renders:

```csharp
    [Fact]
    public void UserDetail_renders_documentation_markdown_when_present()
    {
        // Arrange
        RegisterPipeline();
        var userQuery = Substitute.For<IUserQueryService>();
        userQuery.GetUser("User:alice").Returns(new UserDetailDto(
            Principal: "User:alice",
            HasScramCredentials: true,
            ProducesTopics: [],
            ConsumesTopics: [],
            Groups: [],
            DocumentationPath: "users/alice.md",
            Documentation: "# Alice service"));
        Services.AddSingleton(userQuery);

        // Act
        var cut = Render<UserDetail>(ps => ps.Add(p => p.Principal, "User:alice"));

        // Assert
        Assert.Contains("<h1", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Alice service", cut.Markup, StringComparison.Ordinal);
    }
```

> Note: the "not found" test renders the not-found branch, which does **not** include `MarkdownContent`. Calling `RegisterPipeline()` there anyway is harmless and keeps the tests uniform.

- [ ] **Step 6: Fix `UsersPageTests` (new summary arg) and assert the badge**

In `test/Kafdoc.WebTest/UsersPageTests.cs`, update the `UserSummaryDto` construction to add the trailing flag:

```csharp
            new UserSummaryDto("User:alice", HasScramCredentials: true, ProducesCount: 2, ConsumesCount: 1, HasDocumentation: true),
```

Add a `Docs` assertion to the existing test (after the existing `Assert.Contains`):

```csharp
        Assert.Contains("✓", cut.Markup, StringComparison.Ordinal);
```

- [ ] **Step 7: Fix `TopicsPageTests` (new summary arg)**

In `test/Kafdoc.WebTest/TopicsPageTests.cs`, the `Topics_renders_a_row_per_topic_when_ready` test constructs two summaries. Update them to add the trailing `HasDocumentation` argument:

```csharp
            new TopicSummaryDto("orders", 3, 1, 2, HasDocumentation: true),
            new TopicSummaryDto("billing", 1, 0, 1, HasDocumentation: false),
```

Then add a badge assertion after the existing `Assert.Contains("billing", ...)`:

```csharp
        Assert.Contains("✓", cut.Markup, StringComparison.Ordinal);
```

- [ ] **Step 8: Run the full Web test project**

Run: `dotnet test --no-restore test/Kafdoc.WebTest`
Expected: PASS (all existing tests, updated tests, and new ones).

- [ ] **Step 9: Full solution build (warnings as errors) + full test run**

Run: `dotnet build --no-restore -warnaserror`
Expected: Build succeeded, 0 warnings across all projects.

Run: `dotnet test --no-restore`
Expected: PASS. (The `Kafdoc.InfrastructureTest` Kafka-container tests need a Docker daemon; the new `FileDocumentationStoreTests` do not. If Docker is unavailable, run `dotnet test --no-restore` excluding the container tests, but the documentation-related tests must pass.)

- [ ] **Step 10: Checkpoint** — pause for the user to review/commit.

---

## Manual verification (after Task 5)

1. Create `src/Kafdoc.Web/topics/<some-real-topic>.md` and `src/Kafdoc.Web/users/<some-real-principal-without-User:-prefix>.md` with sample markdown (including a table to confirm advanced extensions).
2. Run `cd src/Kafdoc.Web && dotnet run` (with a configured Kafka section).
3. Visit `/` and `/users` — the documented rows show ✓ in the **Docs** column.
4. Open a documented topic/user detail page — the markdown renders, with a `Source: topics/<name>.md` caption.
5. Open an **undocumented** detail page — it shows *"No additional information available."* and *"Create `topics/<name>.md` to add documentation."* with the correct expected filename.
