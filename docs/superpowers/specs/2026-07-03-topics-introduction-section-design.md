# Topics Index Introduction Section — Design

**Date:** 2026-07-03
**Status:** Approved (pending spec review)

## Summary

Add an operator-authored introduction section to the top of the Topics index
page (route `/`). The content is a single markdown file rendered as HTML — with
working links — so operators can welcome viewers and point them at the internal
source repository where the topic/user markdown docs are authored.

The section renders **inside the page header, between the "Kafka cluster"
eyebrow and the "Topics" title**. When no file exists (or it cannot be read),
the section is omitted entirely and the header looks exactly as it does today.

## Motivation

Kafdoc already supports operator-authored markdown docs for individual topics
and users (`IDocumentationStore` / `FileDocumentationStore`), but there is no
place to explain *how* those docs get added or to give the cluster an
introduction. A viewer landing on `/` has no pointer to the internal repo that
holds the markdown files. A rendered introduction on the landing page closes
that gap.

## Approach

The existing documentation subsystem is keyed by `DocumentationKind`
(`Topic`/`User`) with per-kind folders, filename slugs, front-matter aliases,
and a pattern index. Three options were considered:

- **(A) Extend `IDocumentationStore` with a new `DocumentationKind.Overview`.**
  Rejected: that machinery assumes a *folder of many files* with slug/alias/
  pattern resolution. A single fixed root file does not fit and would require
  special-casing folder/slug/index logic throughout the store.
- **(B) A small dedicated abstraction for the one well-known file.** *Chosen.*
  Mirrors the existing DDD layering (Web → Application → Domain ← Infrastructure)
  with one clear responsibility: read one file.
- **(C) Read the file directly in the Blazor page.** Rejected: breaks the
  Web → Application → Domain layering enforced by `Kafdoc.ArchitectureTest`.

## Components

Following the existing dependency chain **Web → Application → Domain ←
Infrastructure** (Domain has no outbound dependencies).

### Domain — `Kafdoc.Domain.Documentation.IIntroductionSource`

```csharp
/// <summary>Reads the operator-authored introduction markdown for the landing page.</summary>
public interface IIntroductionSource
{
    /// <summary>Reads the introduction markdown.</summary>
    /// <returns>The raw markdown, or <c>null</c> when no file exists or it cannot be read.</returns>
    string? Read();
}
```

XML docs on the public interface and method, per project style.

### Infrastructure — `Kafdoc.Infrastructure.Documentation.FileIntroductionSource`

- Implements `IIntroductionSource`.
- Resolves the file path the same way `FileDocumentationStore` does: takes
  `DocumentationOptions.RootPath` and `IHostEnvironment`; the file lives at
  `{root}/index.md` (root itself, alongside the `topics/` and `users/`
  subfolders).
- Reads the file **live** on each call (`File.ReadAllText`), consistent with how
  `FileDocumentationStore.Read` reads topic/user docs live. No caching/index is
  needed for a single file, and edits show up immediately.
- Missing file → `null`.
- `IOException` / `UnauthorizedAccessException` → `null`, logged via a
  `[LoggerMessage]` partial method (matching the store's logging style).
- Registered as a singleton in `Kafdoc.Infrastructure/Configuration.cs`.

### Application — `IIntroductionQueryService` / `IntroductionQueryService`

- Public interface `IIntroductionQueryService` with `string? GetIntroduction()`.
- `internal sealed` implementation is a thin passthrough to `IIntroductionSource`,
  so the Blazor page consumes an Application service like every other page
  (`ITopicQueryService`, `IUserQueryService`, `ISnapshotStatusService`) rather
  than touching Domain/Infrastructure directly.
- Registered as a singleton in `Kafdoc.Application/Configuration.cs`.

### Web — `Topics.razor` and `MarkdownContent.razor`

**`Topics.razor`** injects `IIntroductionQueryService`, loads the markdown in
`OnInitialized`, and renders it between the eyebrow and the title, only when
non-null:

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

```csharp
@inject IIntroductionQueryService Intro
// ...
private string? _intro;
protected override void OnInitialized() => _intro = Intro.GetIntroduction();
```

**`MarkdownContent.razor`** gains an optional parameter:

```csharp
/// <summary>Whether to show the "Source: &lt;path&gt;" footer. Default true.</summary>
[Parameter]
public bool ShowSource { get; set; } = true;
```

The existing `<p class="doc-source">Source: ...</p>` footer is wrapped in
`@if (ShowSource)` so the intro banner renders as a clean block while topic/user
docs keep their footer. The placeholder branch (when `Markdown is null`) is
unaffected — the Topics page hides the section entirely when null, so that
branch is never reached from the intro path. Prism highlighting
(`OnAfterRenderAsync`) continues to apply.

**`wwwroot/app.css`** gains a `.kd-intro` rule for spacing/visual separation
between the eyebrow and the title (padding/margin; keep consistent with the
existing `.kd-head` / `.documentation` styling).

## Data flow

1. Operator drops `index.md` at the Documentation `RootPath`.
2. Viewer loads `/`; `Topics.OnInitialized` calls `IIntroductionQueryService.GetIntroduction()`.
3. `IntroductionQueryService` delegates to `FileIntroductionSource.Read()`, which reads `{root}/index.md` live.
4. The markdown is rendered to HTML by the shared Markdig pipeline
   (`UseAdvancedExtensions().DisableHtml()`), so markdown links become `<a>`
   elements and any raw HTML is stripped (safe).

No file, or an IO error → `Read()` returns `null` → the section is omitted and
the page is fully functional. This matches the app's "serving beats broken"
philosophy.

## Security / rendering

Rendering reuses the existing singleton `MarkdownPipeline` configured in
`Program.cs` with `.DisableHtml()`, which strips raw HTML while still rendering
markdown links. No new sanitization surface is introduced.

## File location

Fixed file **`index.md`** at the Documentation `RootPath`, alongside the
`topics/` and `users/` subfolders. No new configuration key. (Renaming to
`overview.md` / `introduction.md` is trivial if preferred — decide during spec
review.)

## Testing

- **`FileIntroductionSourceTests`** (`Kafdoc.InfrastructureTest`, temp directory,
  no Docker): returns content when `index.md` exists; returns `null` when it is
  absent. Follows the AAA + snake_case naming convention
  (`Read_returns_content_when_file_exists`,
  `Read_returns_null_when_file_missing`).
- **`IntroductionQueryServiceTests`** (`Kafdoc.ApplicationTest`, NSubstitute):
  passes the source result through
  (`GetIntroduction_returns_source_content`,
  `GetIntroduction_returns_null_when_source_null`).
- **`TopicsPageTests`** (`Kafdoc.WebTest`, bUnit): renders `.kd-intro` with the
  rendered markdown when the service returns content; renders no `.kd-intro`
  when the service returns `null`.
- **`MarkdownContentTests`** (`Kafdoc.WebTest`): `ShowSource="false"` hides the
  source footer; default keeps it.
- **`Kafdoc.ArchitectureTest`**: existing layering/naming rules should stay
  green (new Domain interface, Infrastructure implementation, `*Service`
  naming). Run to confirm.

## Out of scope

- Front matter / aliases / multiple files (single fixed file only).
- Configurable file path or filename (fixed `index.md`).
- Reusing the introduction section on pages other than the Topics index.
- Refreshing the introduction on the snapshot timer (read live per request).

## Non-git note

Per repository `CLAUDE.md`, no git actions are performed by the assistant; the
author reviews and commits changes (including this spec) manually.
