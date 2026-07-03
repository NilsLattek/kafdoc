# Topics Index Introduction Section Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render an operator-authored `index.md` as HTML at the top of the Topics page (between the "Kafka cluster" eyebrow and the "Topics" title), hidden entirely when the file is absent.

**Architecture:** A dedicated single-file abstraction following the existing DDD chain Web → Application → Domain ← Infrastructure. A Domain interface `IIntroductionSource` is implemented in Infrastructure by `FileIntroductionSource` (reads `{RootPath}/index.md` live), surfaced through an Application `IIntroductionQueryService` passthrough, and consumed by `Topics.razor` which reuses the existing `MarkdownContent` component (extended with a `ShowSource` flag).

**Tech Stack:** .NET 10, C#, Blazor Server (InteractiveServer), Markdig, xUnit v3 (Microsoft.Testing.Platform), bUnit, NSubstitute.

## Global Constraints

- Target framework `net10.0`; nullable enabled; analyzers (Meziantou, SonarAnalyzer, Roslynator) run on build; CI builds with `-warnaserror` — treat warnings as errors.
- Central package management: no version-bearing `PackageReference`; no new packages are needed for this plan.
- No git actions beyond the per-task commits below are performed by the assistant unless the author asks; the author reviews and commits. (Repo `CLAUDE.md`: "Do not perform any git actions".) The commit steps in this plan are written for the author/executor to run.
- C#: 4-space indent, `PascalCase` types/methods, `_camelCase` private fields, primary constructors where possible, XML comments on all public classes/methods/properties/fields.
- Tests: `<ClassName>Tests`; test methods `snake_case` describing behavior (never `Async` suffix); Arrange/Act/Assert with a comment per section.
- Register services in the owning project's `Configuration.cs`, never in `Program.cs`.
- The introduction file is the fixed path `{DocumentationOptions.RootPath}/index.md` (root itself, alongside `topics/` and `users/`). No new config key.
- Build: `dotnet build --no-restore -warnaserror`. Tests: `dotnet test --no-restore <project>`.

---

### Task 1: Domain abstraction `IIntroductionSource`

**Files:**
- Create: `src/Kafdoc.Domain/Documentation/IIntroductionSource.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `Kafdoc.Domain.Documentation.IIntroductionSource` with `string? Read()`.

- [ ] **Step 1: Create the interface**

Create `src/Kafdoc.Domain/Documentation/IIntroductionSource.cs`:

```csharp
namespace Kafdoc.Domain.Documentation;

/// <summary>Reads the operator-authored introduction markdown shown on the Topics landing page.</summary>
public interface IIntroductionSource
{
    /// <summary>Reads the introduction markdown.</summary>
    /// <returns>The raw markdown, or <c>null</c> when no file exists or it cannot be read.</returns>
    string? Read();
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build --no-restore -warnaserror src/Kafdoc.Domain`
Expected: PASS (Build succeeded, 0 warnings).

- [ ] **Step 3: Commit**

```bash
git add src/Kafdoc.Domain/Documentation/IIntroductionSource.cs
git commit -m "feat: add IIntroductionSource domain abstraction"
```

---

### Task 2: Infrastructure `FileIntroductionSource`

**Files:**
- Create: `src/Kafdoc.Infrastructure/Documentation/FileIntroductionSource.cs`
- Modify: `src/Kafdoc.Infrastructure/Configuration.cs` (add registration)
- Test: `test/Kafdoc.InfrastructureTest/Documentation/FileIntroductionSourceTests.cs`

**Interfaces:**
- Consumes: `IIntroductionSource` (Task 1); `DocumentationOptions.RootPath` (existing, `src/Kafdoc.Infrastructure/Documentation/DocumentationOptions.cs`).
- Produces: `Kafdoc.Infrastructure.Documentation.FileIntroductionSource : IIntroductionSource`; DI registration `IIntroductionSource` → `FileIntroductionSource`.

- [ ] **Step 1: Write the failing tests**

Create `test/Kafdoc.InfrastructureTest/Documentation/FileIntroductionSourceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --no-restore test/Kafdoc.InfrastructureTest --filter-class "*FileIntroductionSourceTests*"`
Expected: FAIL (does not compile — `FileIntroductionSource` does not exist).

- [ ] **Step 3: Create `FileIntroductionSource`**

Create `src/Kafdoc.Infrastructure/Documentation/FileIntroductionSource.cs`:

```csharp
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
```

- [ ] **Step 4: Register the service**

In `src/Kafdoc.Infrastructure/Configuration.cs`, immediately after the existing line
`services.AddSingleton<IDocumentationStore, FileDocumentationStore>();`, add:

```csharp
        services.AddSingleton<IIntroductionSource, FileIntroductionSource>();
```

(`DocumentationOptions` is already bound just above; no extra binding needed.)

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --no-restore test/Kafdoc.InfrastructureTest --filter-class "*FileIntroductionSourceTests*"`
Expected: PASS (2 passed).

- [ ] **Step 6: Build the Infrastructure project with warnings as errors**

Run: `dotnet build --no-restore -warnaserror src/Kafdoc.Infrastructure`
Expected: PASS (0 warnings).

- [ ] **Step 7: Commit**

```bash
git add src/Kafdoc.Infrastructure/Documentation/FileIntroductionSource.cs src/Kafdoc.Infrastructure/Configuration.cs test/Kafdoc.InfrastructureTest/Documentation/FileIntroductionSourceTests.cs
git commit -m "feat: read introduction markdown from index.md"
```

---

### Task 3: Application `IIntroductionQueryService`

**Files:**
- Create: `src/Kafdoc.Application/Services/IIntroductionQueryService.cs`
- Create: `src/Kafdoc.Application/Services/IntroductionQueryService.cs`
- Modify: `src/Kafdoc.Application/Configuration.cs` (add registration)
- Test: `test/Kafdoc.ApplicationTest/Services/IntroductionQueryServiceTests.cs`

**Interfaces:**
- Consumes: `IIntroductionSource` (Task 1).
- Produces: `Kafdoc.Application.Services.IIntroductionQueryService` with `string? GetIntroduction()`; DI registration.

- [ ] **Step 1: Write the failing tests**

Create `test/Kafdoc.ApplicationTest/Services/IntroductionQueryServiceTests.cs`:

```csharp
using Kafdoc.Application.Services;
using Kafdoc.Domain.Documentation;

using NSubstitute;

namespace Kafdoc.ApplicationTest.Services;

public class IntroductionQueryServiceTests
{
    [Fact]
    public void GetIntroduction_returns_source_content()
    {
        // Arrange
        var source = Substitute.For<IIntroductionSource>();
        source.Read().Returns("# Hello");
        var service = new IntroductionQueryService(source);

        // Act
        var result = service.GetIntroduction();

        // Assert
        Assert.Equal("# Hello", result);
    }

    [Fact]
    public void GetIntroduction_returns_null_when_source_null()
    {
        // Arrange
        var source = Substitute.For<IIntroductionSource>();
        source.Read().Returns((string?)null);
        var service = new IntroductionQueryService(source);

        // Act
        var result = service.GetIntroduction();

        // Assert
        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --no-restore test/Kafdoc.ApplicationTest --filter-class "*IntroductionQueryServiceTests*"`
Expected: FAIL (does not compile — service types do not exist).

- [ ] **Step 3: Create the interface**

Create `src/Kafdoc.Application/Services/IIntroductionQueryService.cs`:

```csharp
namespace Kafdoc.Application.Services;

/// <summary>Provides the introduction markdown for the Topics landing page.</summary>
public interface IIntroductionQueryService
{
    /// <summary>Gets the introduction markdown.</summary>
    /// <returns>The raw markdown, or <c>null</c> when none is authored.</returns>
    string? GetIntroduction();
}
```

- [ ] **Step 4: Create the implementation**

Create `src/Kafdoc.Application/Services/IntroductionQueryService.cs`:

```csharp
using Kafdoc.Domain.Documentation;

namespace Kafdoc.Application.Services;

/// <summary>Reads the introduction markdown from the documentation source.</summary>
/// <param name="source">The introduction source.</param>
internal sealed class IntroductionQueryService(IIntroductionSource source) : IIntroductionQueryService
{
    /// <inheritdoc />
    public string? GetIntroduction() => source.Read();
}
```

- [ ] **Step 5: Register the service**

In `src/Kafdoc.Application/Configuration.cs`, immediately after
`services.AddSingleton<Services.ISnapshotStatusService, Services.SnapshotStatusService>();`, add:

```csharp
        services.AddSingleton<Services.IIntroductionQueryService, Services.IntroductionQueryService>();
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --no-restore test/Kafdoc.ApplicationTest --filter-class "*IntroductionQueryServiceTests*"`
Expected: PASS (2 passed).

- [ ] **Step 7: Build the Application project with warnings as errors**

Run: `dotnet build --no-restore -warnaserror src/Kafdoc.Application`
Expected: PASS (0 warnings).

- [ ] **Step 8: Commit**

```bash
git add src/Kafdoc.Application/Services/IIntroductionQueryService.cs src/Kafdoc.Application/Services/IntroductionQueryService.cs src/Kafdoc.Application/Configuration.cs test/Kafdoc.ApplicationTest/Services/IntroductionQueryServiceTests.cs
git commit -m "feat: expose introduction markdown via query service"
```

---

### Task 4: `MarkdownContent` gains a `ShowSource` flag

**Files:**
- Modify: `src/Kafdoc.Web/Components/Shared/MarkdownContent.razor`
- Test: `test/Kafdoc.WebTest/MarkdownContentTests.cs`

**Interfaces:**
- Consumes: existing `MarkdownContent` (`Markdown`, `Path` parameters).
- Produces: new optional parameter `bool ShowSource` (default `true`) on `MarkdownContent` that suppresses the `Source:` footer when `false`.

- [ ] **Step 1: Write the failing test**

The existing `MarkdownContentTests` class has a `RegisterPipeline()` helper (sets `JSInterop.Mode = JSRuntimeMode.Loose` and registers the Markdig pipeline). Add this test to the class, reusing that helper:

```csharp
    [Fact]
    public void Hides_source_caption_when_ShowSource_is_false()
    {
        // Arrange
        RegisterPipeline();

        // Act
        var cut = Render<MarkdownContent>(ps => ps
            .Add(p => p.Markdown, "# Hello")
            .Add(p => p.Path, "index.md")
            .Add(p => p.ShowSource, false));

        // Assert
        Assert.DoesNotContain("doc-source", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Source:", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Hello", cut.Markup, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --no-restore test/Kafdoc.WebTest --filter-class "*MarkdownContentTests*"`
Expected: FAIL (does not compile — `ShowSource` parameter does not exist).

- [ ] **Step 3: Add the `ShowSource` parameter and gate the footer**

In `src/Kafdoc.Web/Components/Shared/MarkdownContent.razor`, change the rendered branch so the source footer is conditional. Replace:

```razor
        <div class="doc-body" @ref="_docBody">@((MarkupString)Markdig.Markdown.ToHtml(Markdown, Pipeline))</div>
        <p class="doc-source">Source: <code>@Path</code></p>
```

with:

```razor
        <div class="doc-body" @ref="_docBody">@((MarkupString)Markdig.Markdown.ToHtml(Markdown, Pipeline))</div>
        @if (ShowSource)
        {
            <p class="doc-source">Source: <code>@Path</code></p>
        }
```

Then, in the `@code` block, add the parameter after the existing `Path` property:

```csharp
    /// <summary>Whether to show the "Source: &lt;path&gt;" footer. Default <c>true</c>.</summary>
    [Parameter]
    public bool ShowSource { get; set; } = true;
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --no-restore test/Kafdoc.WebTest --filter-class "*MarkdownContentTests*"`
Expected: PASS (all MarkdownContent tests pass, including the new one).

- [ ] **Step 5: Commit**

```bash
git add src/Kafdoc.Web/Components/Shared/MarkdownContent.razor test/Kafdoc.WebTest/MarkdownContentTests.cs
git commit -m "feat: add ShowSource flag to MarkdownContent"
```

---

### Task 5: Render the introduction on the Topics page

**Files:**
- Modify: `src/Kafdoc.Web/Components/Pages/Topics.razor`
- Modify: `src/Kafdoc.Web/wwwroot/app.css` (add `.kd-intro`)
- Modify: `test/Kafdoc.WebTest/TopicsPageTests.cs` (register the new service in existing tests + add two tests)

**Interfaces:**
- Consumes: `IIntroductionQueryService.GetIntroduction()` (Task 3); `MarkdownContent` with `ShowSource` (Task 4).
- Produces: introduction markup wrapped in `<div class="kd-intro">` between the eyebrow and the title, rendered only when `GetIntroduction()` is non-null.

- [ ] **Step 1: Update existing Topics tests to register the new required service**

Both existing tests in `test/Kafdoc.WebTest/TopicsPageTests.cs` render `<Topics>`, which will now `@inject IIntroductionQueryService`. Without a registration the render throws. In **each** existing test's Arrange section (both `Topics_renders_loading_message_when_snapshot_not_ready` and `Topics_renders_a_row_per_topic_when_ready`), add these two lines alongside the existing `Services.AddSingleton(...)` calls:

```csharp
        var intro = Substitute.For<IIntroductionQueryService>();
        Services.AddSingleton(intro);
```

- [ ] **Step 2: Write the two failing tests**

Add to the `TopicsPageTests` class in `test/Kafdoc.WebTest/TopicsPageTests.cs`. These need the Markdig `MarkdownPipeline` registered (because the intro renders `MarkdownContent`) and a JS interop stub for Prism. Register the pipeline the same way `MarkdownContentTests` does, and set bUnit's JSInterop to loose mode so the Prism call is a no-op:

```csharp
    [Fact]
    public void Topics_renders_introduction_when_present()
    {
        // Arrange
        JSInterop.Mode = JSRuntimeMode.Loose;
        var topicQuery = Substitute.For<ITopicQueryService>();
        topicQuery.GetTopics().Returns(System.Array.Empty<TopicSummaryDto>());
        var status = Substitute.For<ISnapshotStatusService>();
        status.GetStatus().Returns(new SnapshotStatusDto(IsReady: true, LastRefresh: DateTimeOffset.UnixEpoch, LastError: null));
        var intro = Substitute.For<IIntroductionQueryService>();
        intro.GetIntroduction().Returns("Add docs in [our repo](https://example.com/repo).");
        Services.AddSingleton(topicQuery);
        Services.AddSingleton(status);
        Services.AddSingleton(intro);
        Services.AddSingleton(new Markdig.MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build());

        // Act
        var cut = Render<Topics>();

        // Assert
        Assert.Contains("kd-intro", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("href=\"https://example.com/repo\"", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Topics_omits_introduction_when_absent()
    {
        // Arrange
        JSInterop.Mode = JSRuntimeMode.Loose;
        var topicQuery = Substitute.For<ITopicQueryService>();
        topicQuery.GetTopics().Returns(System.Array.Empty<TopicSummaryDto>());
        var status = Substitute.For<ISnapshotStatusService>();
        status.GetStatus().Returns(new SnapshotStatusDto(IsReady: true, LastRefresh: DateTimeOffset.UnixEpoch, LastError: null));
        var intro = Substitute.For<IIntroductionQueryService>();
        intro.GetIntroduction().Returns((string?)null);
        Services.AddSingleton(topicQuery);
        Services.AddSingleton(status);
        Services.AddSingleton(intro);
        Services.AddSingleton(new Markdig.MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build());

        // Act
        var cut = Render<Topics>();

        // Assert
        Assert.DoesNotContain("kd-intro", cut.Markup, StringComparison.Ordinal);
    }
```

If the file lacks the `using Bunit;` for `JSRuntimeMode`/`JSInterop`, note `TopicsPageTests` already derives from `Bunit.BunitContext` and `using Bunit;` is present at the top — no new using needed.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --no-restore test/Kafdoc.WebTest --filter-class "*TopicsPageTests*"`
Expected: FAIL (`Topics` does not yet inject `IIntroductionQueryService` / render `kd-intro`; the intro assertions fail).

- [ ] **Step 4: Inject and load the introduction in `Topics.razor`**

In `src/Kafdoc.Web/Components/Pages/Topics.razor`, add the injection after the existing injects (below `@inject ISnapshotStatusService Status`):

```razor
@inject IIntroductionQueryService Intro
```

In the `@code` block, add the field and `OnInitialized` (place the field next to `_filter`):

```csharp
    private string? _intro;

    protected override void OnInitialized() => _intro = Intro.GetIntroduction();
```

- [ ] **Step 5: Render the introduction inside the header**

In the same file, change the header so the intro renders between the eyebrow and the title. Replace:

```razor
    <header class="kd-head">
        <p class="kd-eyebrow">Kafka cluster</p>
        <h1 class="kd-title">Topics</h1>
        <p class="kd-sub">Producers, consumer groups, and docs for every topic in the snapshot.</p>
    </header>
```

with:

```razor
    <header class="kd-head">
        <p class="kd-eyebrow">Kafka cluster</p>
        @if (_intro is not null)
        {
            <div class="kd-intro">
                <MarkdownContent Markdown="@_intro" Path="index.md" ShowSource="false" />
            </div>
        }
        <h1 class="kd-title">Topics</h1>
        <p class="kd-sub">Producers, consumer groups, and docs for every topic in the snapshot.</p>
    </header>
```

- [ ] **Step 6: Add `.kd-intro` styling**

In `src/Kafdoc.Web/wwwroot/app.css`, immediately after the `.kd-eyebrow { ... }` rule (ends at the line with the closing brace before `.kd-title`), add:

```css
.kd-intro {
    margin: 0.35rem 0 0.9rem;
}
```

(The nested `.documentation` block already styles the rendered body via `MarkdownContent`; `.kd-intro` only controls spacing between the eyebrow and the title.)

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test --no-restore test/Kafdoc.WebTest --filter-class "*TopicsPageTests*"`
Expected: PASS (all four TopicsPageTests pass — two updated, two new).

- [ ] **Step 8: Commit**

```bash
git add src/Kafdoc.Web/Components/Pages/Topics.razor src/Kafdoc.Web/wwwroot/app.css test/Kafdoc.WebTest/TopicsPageTests.cs
git commit -m "feat: render introduction section on Topics page"
```

---

### Task 6: Full build and test sweep

**Files:** none (verification only).

- [ ] **Step 1: Restore then build the whole solution with warnings as errors**

Run: `dotnet build --no-restore -warnaserror`
Expected: PASS (Build succeeded, 0 warnings). If restore is stale, run `dotnet restore` first.

- [ ] **Step 2: Run the full unit/component/architecture test suite**

Run: `dotnet test --no-restore test/Kafdoc.DomainTest test/Kafdoc.ApplicationTest test/Kafdoc.WebTest test/Kafdoc.ArchitectureTest`
Expected: PASS (all tests pass; the ArchitectureTest layering/naming rules stay green with the new Domain interface, Infrastructure implementation, and `*Service`).

- [ ] **Step 3: (Optional) Manual smoke check**

Create `index.md` at the configured `Documentation:RootPath` with, e.g.:

```markdown
# Welcome to the Kafka catalog

Add topic and user docs in [our internal repo](https://git.internal/kafka-docs).
```

Run `cd src/Kafdoc.Web && dotnet run`, open `/`, and confirm the rendered intro (with a working link) appears between the "Kafka cluster" eyebrow and the "Topics" title, and that removing the file hides the section.

- [ ] **Step 4: Commit (only if Step 3 produced tracked changes; otherwise skip)**

No source changes are expected from this task; nothing to commit unless a manual tweak was made.

---

## Notes for the executor

- **Do not** run `git push` or open a PR — the author handles all remote git actions (`CLAUDE.md`).
- The `MarkdownPipeline` is a singleton registered in `Program.cs` for the running app; the bUnit tests register their own equivalent pipeline (`UseAdvancedExtensions().DisableHtml()`), which is why Task 5's tests add it to `Services`.
- Reading `index.md` live per request is intentional (matches `FileDocumentationStore`); there is no cache to invalidate and edits appear on the next page load.
