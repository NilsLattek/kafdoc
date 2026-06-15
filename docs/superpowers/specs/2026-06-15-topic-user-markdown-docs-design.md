# Markdown documentation for topics & users — design

**Date:** 2026-06-15
**Status:** Approved (design)

## Goal

Let operators attach free-form documentation to individual Kafka topics and users
(principals) by dropping Markdown files into the deployment. Detail pages render the
matching file; overview pages flag which topics/users already have docs so gaps are
easy to spot. Files are read on demand from disk, so operators can bake docs into a
derived Docker image:

```dockerfile
FROM kafdoc:1.0.0
COPY topics/ /app/topics
COPY users/ /app/users
```

Non-goals: editing docs from the UI, a database, cross-linking/search inside docs,
hot-reload notifications. Docs are static content read fresh on each page view.

## Approach

Approach A from brainstorming: documentation is a **Domain port** with an
**Infrastructure file adapter**, folded into the existing query services and DTOs;
**Web** owns Markdown→HTML rendering. This mirrors the existing `IKafkaClusterReader`
pattern (port in Domain, adapter in Infrastructure) and introduces no new layering
conventions. Markdig stays in the Web layer only.

## 1. Configuration

A new `Documentation` config section, bound to `DocumentationOptions` in
Infrastructure (`Documentation/DocumentationOptions.cs`):

```jsonc
"Documentation": {
  "RootPath": ""   // relative to the content root; "" = app root.
}                  // topics/ and users/ subfolders live under RootPath.
```

- `RootPath` defaults to `""` (content root), so the Dockerfile above works as written
  (`/app/topics`, `/app/users`). An operator who prefers a subfolder sets
  `"RootPath": "docs"`, giving `docs/topics` and `docs/users`.
- A relative `RootPath` resolves against `IHostEnvironment.ContentRootPath`; an
  absolute path is used as-is.

## 2. Name → file mapping (pure Domain logic)

A pure static helper `DocumentationSlug` in `Kafdoc.Domain/Documentation/`:

- **Topic** `orders.placed` → relative path `topics/orders.placed.md`
- **User** `User:svc-payments` → relative path `users/svc-payments.md`
  (the `User:` prefix is stripped before slugging; matching is ordinal/case-sensitive
  on the prefix).
- **`Slug(string)`**: replace each OS-illegal character (`< > : " / \ | ? *`) with `_`,
  and strip leading `.` characters. Dots elsewhere are preserved
  (`orders.placed` stays `orders.placed`). This makes traversal sequences inert.

API (all pure, no I/O):

```csharp
public static class DocumentationSlug
{
    public static string ForTopic(string topicName);   // -> "orders.placed"  (slug only, no folder/ext)
    public static string ForUser(string principal);    // -> "svc-payments"
}
```

The folder + `.md` extension are applied by the adapter (§3); the helper returns the
bare slug so both Application (membership checks) and Infrastructure (path building)
share one definition.

**Slug collisions:** two distinct names can slug to the same file (e.g. `a:b` and
`a/b`). This is accepted as a rare edge case and not resolved; first match wins.

## 3. Domain port + Infrastructure adapter

**Domain** (`Kafdoc.Domain/Documentation/`):

```csharp
public enum DocumentationKind { Topic, User }

/// RelativePath is ALWAYS populated (e.g. "topics/orders.placed.md").
/// Content is null when no file exists.
public sealed record DocumentationLookup(string RelativePath, string? Content);

public interface IDocumentationStore
{
    DocumentationLookup Read(DocumentationKind kind, string name);
    IReadOnlySet<string> ListSlugs(DocumentationKind kind);   // slugs present on disk
}
```

**Infrastructure** (`Kafdoc.Infrastructure/Documentation/FileDocumentationStore.cs`):

- `Read`: compute slug via `DocumentationSlug`, build
  `{RootPath}/{topics|users}/{slug}.md`, return `RelativePath` always.
  - File present → read all text into `Content`.
  - File/dir absent → `Content = null` (no throw).
  - **Traversal guard:** resolve the candidate to a full path and verify it is still
    under the intended subfolder; if not, treat as absent.
  - `IOException` / `UnauthorizedAccessException` → logged at Warning, treated as
    absent so the page still renders. The catch is narrow (specific exception types),
    not a catch-all.
- `ListSlugs`: enumerate `*.md` in the relevant subfolder (one directory read per
  call), return the file names without extension as a `HashSet<string>(StringComparer.Ordinal)`.
  Missing directory → empty set.
- `RelativePath` uses forward slashes for stable display regardless of OS.

Registered in `Kafdoc.Infrastructure/Configuration.cs`: bind `DocumentationOptions`
from the `Documentation` section; `services.AddSingleton<IDocumentationStore,
FileDocumentationStore>()`.

## 4. Application — fold into existing query services & DTOs

`TopicQueryService` and `UserQueryService` take `IDocumentationStore` via their primary
constructors (implementations stay `internal`, per `ApplicationServiceTests`).

**Detail DTOs** gain two fields:

```csharp
public sealed record TopicDetailDto(
    string Name,
    int PartitionCount,
    IReadOnlyList<string> Producers,
    IReadOnlyList<TopicConsumerDto> ConsumerGroups,
    IReadOnlyList<string> ReadOnlyPrincipals,
    string DocumentationPath,        // always, e.g. "topics/orders.placed.md"
    string? Documentation);          // raw markdown, null if file absent

public sealed record UserDetailDto(
    string Principal,
    bool HasScramCredentials,
    IReadOnlyList<string> ProducesTopics,
    IReadOnlyList<string> ConsumesTopics,
    IReadOnlyList<string> Groups,
    string DocumentationPath,        // always, e.g. "users/svc-payments.md"
    string? Documentation);
```

`GetTopic`/`GetUser` call `store.Read(kind, name)` and copy `RelativePath` →
`DocumentationPath`, `Content` → `Documentation`.

**Summary DTOs** gain a flag:

```csharp
public sealed record TopicSummaryDto(string Name, int PartitionCount,
    int ProducerCount, int ConsumerGroupCount, bool HasDocumentation);

public sealed record UserSummaryDto(string Principal, bool HasScramCredentials,
    int ProducesCount, int ConsumesCount, bool HasDocumentation);
```

`GetTopics`/`GetUsers` call `store.ListSlugs(kind)` **once** per render, then per row set
`HasDocumentation = slugs.Contains(DocumentationSlug.ForTopic(name))` (resp. `ForUser`).

## 5. Web — rendering + filename display

A reusable component `Components/Shared/MarkdownContent.razor`:

- Parameters: `string? Markdown`, `string Path`.
- A `MarkdownPipeline` singleton (`new MarkdownPipelineBuilder().UseAdvancedExtensions()
  .DisableHtml().Build()`) is injected — advanced extensions give tables, fenced code,
  autolinks, task lists; `DisableHtml()` escapes raw inline HTML (rich + sanitized).
  Registered in `Program.cs` (the composition root, exempt from `WebIsolationTests`).
- **Content present** → `@((MarkupString)Markdown.ToHtml(pipeline))` inside a styled
  block, with a small caption `Source: {Path}`.
- **Content null** → *"No additional information available."* followed by
  *"Create `{Path}` to add documentation."* — the expected filename is shown **either
  way**, so authors learn what to name a new file.

The component handles only `string`s (no Domain/Infrastructure types), so the Web
isolation rule holds.

Wiring:
- `TopicDetail.razor` / `UserDetail.razor`: add a "Documentation" section with
  `<MarkdownContent Markdown="@detail.Documentation" Path="@detail.DocumentationPath" />`.
- `Topics.razor` / `Users.razor`: add a "Docs" column rendering `✓` when
  `HasDocumentation`, blank otherwise.

## 6. Error handling

- Missing file or directory → `Content = null`; pages render the fallback. Not an error.
- IO / permission errors → logged at Warning, degraded to "no doc".
- Path traversal → neutralized by slug rules and re-checked by the resolved-path guard.
- Empty/whitespace name → treated as no doc (no file lookup).

## 7. Testing

- **Domain** (`Kafdoc.DomainTest`): `DocumentationSlug` — `User:` prefix strip, illegal
  char replacement, leading-dot/traversal stripping, dots preserved.
- **Infrastructure** (`Kafdoc.InfrastructureTest`): `FileDocumentationStore` against a
  temp directory — read hit, read miss, missing directory, traversal blocked,
  `ListSlugs` content, forward-slash `RelativePath`.
- **Application** (`Kafdoc.ApplicationTest`): query services with a substitute
  `IDocumentationStore` — `HasDocumentation` set correctly from `ListSlugs`; detail DTO
  carries `Documentation` content and `DocumentationPath`; `ListSlugs` invoked once.
- **Web** (`Kafdoc.WebTest`, bUnit): `MarkdownContent` renders HTML for content and the
  filename-bearing fallback for null; detail pages embed the section; overview pages
  show the badge.
- **Architecture** (`Kafdoc.ArchitectureTest`): existing layering/isolation rules must
  still pass (port in Domain, adapter in Infrastructure, Web touches only DTOs).

## Files touched (summary)

- **New:** `Domain/Documentation/{DocumentationKind,DocumentationLookup,IDocumentationStore,DocumentationSlug}.cs`;
  `Infrastructure/Documentation/{DocumentationOptions,FileDocumentationStore}.cs`;
  `Web/Components/Shared/MarkdownContent.razor`; matching test files.
- **Changed:** `TopicDetailDto`, `UserDetailDto`, `TopicSummaryDto`, `UserSummaryDto`;
  `TopicQueryService`, `UserQueryService`; `Infrastructure/Configuration.cs`;
  `Web/Program.cs`; `TopicDetail.razor`, `UserDetail.razor`, `Topics.razor`,
  `Users.razor`; `appsettings.json`; `Directory.Packages.props` (add Markdig).
