# Sharing one markdown doc across branch-variant topics & users — design

**Date:** 2026-06-16
**Status:** Approved (design)

## Goal

The same logical Kafka topic often exists once per branch, under distinct names that
differ only by a branch token (e.g. `orders.branch01.placed`, `orders.branch07.placed`).
These instances share an identical message structure and should share **one** markdown
documentation file instead of forcing a near-duplicate file per branch. The same need
applies to branch-variant service principals.

The branch token's shape **varies per topic**, so no single normalization rule can
strip it. Sharing is therefore *declared* per logical topic, in the documentation
itself, via optional YAML front matter (Approach A from brainstorming).

This builds on the existing markdown-docs feature
(`2026-06-15-topic-user-markdown-docs-design.md`): a Domain port `IDocumentationStore`
+ pure `DocumentationSlug`, an Infrastructure `FileDocumentationStore`, Markdig
rendering in Web. Markdig now also runs in Infrastructure to locate the front-matter
block (no hand-rolled `---` splitting); no architecture rule pins it to a layer. Files
are baked into a Docker image and are immutable at runtime.

Non-goals: editing docs or front matter from the UI; a "rebuild index" button (designed
for, not built); caching file *content* in memory; regex/normalization-based matching.

## Approach

Each markdown file may declare, in YAML front matter, additional topic/user names it
documents — exact names or `*` globs. The **filename remains the primary key**; front
matter adds aliases on top. An immutable in-memory **index** is built once (lazily, on
first access) so list/badge queries are pure index lookups with zero file I/O — the
files cannot change inside the container, so a one-time scan is safe.

## 1. Authoring model

A topic doc with front matter:

```markdown
---
topics:
  - orders.*.placed        # glob: covers orders.branch01.placed, orders.branch07.placed, …
  - legacy.orders.placed   # plain name: exact match
---
# Orders placed

Shared documentation for the orders.placed event across all branches.
```

- The **filename** is still the primary key: `orders.placed.md` documents the topic
  `orders.placed` by name, exactly as today. Front matter adds *aliases* on top.
- The filename may be arbitrary when no single real topic is the natural home, e.g.
  `orders-shared.md` whose front matter lists `orders.*.placed`. Matching is by
  filename-slug **or** any front-matter pattern.
- **Topics and users are symmetric.** Docs under `topics/` use a `topics:` key; docs
  under `users/` use a `users:` key. User names are matched after the `User:` type
  prefix is stripped (same rule `DocumentationSlug.ForUser` already applies).
- Front matter is optional; a file with none behaves exactly as today.
- The front-matter block is **never rendered**: the Web Markdig pipeline enables
  `UseYamlFrontMatter()`, which omits the block from the HTML output (see §3/§5).

### Glob semantics

- `*` matches any run of characters, **including dots** (`orders.*.placed` matches
  `orders.branch01.placed`).
- A pattern containing no `*` is an exact, ordinal, case-sensitive match.
- Patterns are matched against the **raw** topic name / `User:`-stripped principal,
  not the slug (real Kafka names are already slug-safe).

### Precedence & conflicts

1. A topic/user's **own file** (`BySlug` hit) always wins over any pattern.
2. Among patterns, files are processed in **ordinal-sorted order by relative path**;
   the **first** matching file wins (deterministic).
3. A name matching patterns in two *different* files logs a **Warning** (the first
   still wins). This surfaces accidental overlap without failing the page.

## 2. In-memory index (performance core)

A new immutable type, built once per kind:

```csharp
internal sealed class DocumentationIndex
{
    // slug (filename without extension) -> relative path, e.g. "orders.placed" -> "topics/orders.placed.md"
    public IReadOnlyDictionary<string, string> BySlug { get; }

    // ordered (glob, relative path) gathered from front matter across all files
    public IReadOnlyList<(string Pattern, string RelativePath)> Patterns { get; }
}
```

- Built **lazily on first access**, held behind a `volatile` reference and swapped
  atomically — mirroring the existing `ISnapshotStore` immutable-swap convention.
- `Rebuild()` builds a fresh index and swaps the reference. No UI is added now, but a
  future "rebuild index" button is a one-line call against this method. A startup
  warm-up (resolving the store once in `Program.cs`) is optional and may be wired later.
- The index stores **mappings only**, never file content. Detail pages read the single
  resolved file on demand — small files, negligible I/O — keeping memory low while
  eliminating the per-list-render full-folder scan.

### Resolution: name → file

1. Compute `slug = DocumentationSlug.For(kind, name)`. If `BySlug` contains it → that
   file (direct/primary match).
2. Else the **first** `Patterns` entry whose glob matches the raw name → that file.
3. Else → no documentation.

## 3. Domain port changes

`IDocumentationStore.ListSlugs` cannot express pattern matches and is **replaced** by a
lightweight, content-free check:

```csharp
public interface IDocumentationStore
{
    /// Detail: resolved path + raw file content, or the entity's own expected path +
    /// null when nothing matches.
    DocumentationLookup Read(DocumentationKind kind, string name);

    /// List badge: true when the name resolves to a doc. Index lookup, zero I/O,
    /// no content load.
    bool HasDocumentation(DocumentationKind kind, string name);
}
```

- `Read` resolves via the index and reads **that one file**, returning its **raw
  content** (front matter included). The front-matter block is not stripped here — the
  Web render pipeline's `UseYamlFrontMatter()` omits it from the rendered HTML, so
  stripping in the adapter is unnecessary.
  - On a **pattern** match, `RelativePath` points at the **resolved/shared file** (e.g.
    `topics/orders.placed.md`), so the detail page's `Source: …` caption naturally
    signals the doc is shared.
  - On **no** match, `RelativePath` stays the entity's *own* expected path, so the
    existing "Create `{Path}` to add documentation." hint remains correct.
- `DocumentationLookup` is unchanged.

## 4. Pure matching helper (Domain)

A pure, dependency-free, unit-tested helper alongside `DocumentationSlug`:

```csharp
public static class DocumentationPattern
{
    /// Ordinal, case-sensitive '*' glob match ('*' spans any characters incl. dots).
    /// A pattern with no '*' is an exact match.
    public static bool Matches(string pattern, string name);
}
```

Front-matter parsing and file I/O stay out of Domain.

## 5. Infrastructure adapter

`FileDocumentationStore` (still a singleton) gains:

- **Index build** (`BuildIndex`, invoked lazily; re-invokable via `Rebuild`):
  - Enumerate `*.md` in `topics/` and `users/`.
  - For each file, parse it with a small Infrastructure-local Markdig pipeline
    (`new MarkdownPipelineBuilder().UseYamlFrontMatter().Build()`) and pull the
    `YamlFrontMatterBlock` (if any). Record the filename slug in `BySlug`, and each
    `topics:`/`users:` entry (per folder) in `Patterns`.
  - Order `Patterns` by ordinal relative path so resolution is a deterministic
    first-match. When a name matches patterns in more than one file, resolution emits a
    Warning (the first still wins).
  - Malformed/unparseable front matter → logged at Warning, treated as **no aliases**
    (the file's own slug still indexes); never throws.
- **Front-matter handling:** **Markdig** locates the front-matter block (replacing
  hand-rolled `---` splitting); **YamlDotNet** deserializes that block's text into the
  alias list (robust to quoting, flow lists, comments). Both are confined to
  Infrastructure; Markdig is already pinned in `Directory.Packages.props`, so only a
  project reference is added.
- **`Read`:** resolve via the index; read the resolved file; return its **raw content**
  as `Content` (no front-matter stripping — the Web pipeline omits it at render). Same
  traversal guard, narrow `IOException` / `UnauthorizedAccessException` handling, and
  forward-slash `RelativePath` as today.
- **`HasDocumentation`:** resolve via the index; return whether a file matched. No
  content read.

Registration in `Infrastructure/Configuration.cs` is unchanged
(`AddSingleton<IDocumentationStore, FileDocumentationStore>()`); add a Markdig project
reference to `Kafdoc.Infrastructure.csproj` and the YamlDotNet package version to
`Directory.Packages.props`.

## 6. Application changes

- `TopicQueryService.GetTopics` / `UserQueryService.GetUsers`: replace the single
  `ListSlugs` call + `slugs.Contains(...)` membership with a per-row
  `documentation.HasDocumentation(kind, name)` (index lookups, no scanning).
- `GetTopic` / `GetUser`: unchanged — still call `Read(kind, name)` and copy
  `RelativePath`/`Content`.
- DTOs are unchanged.

## 6a. Web change

Add `.UseYamlFrontMatter()` to the `MarkdownPipelineBuilder` registered in
`Program.cs` (currently `UseAdvancedExtensions().DisableHtml()`). With the extension
enabled, Markdig keeps any leading `--- … ---` block out of the rendered HTML, so the
raw `Content` returned by `Read` displays cleanly without the adapter stripping it. The
`MarkdownContent` component is otherwise unchanged. The bUnit test setup builds its own
pipeline and gains the same `.UseYamlFrontMatter()` call.

## 7. Error handling

- Missing file/dir → `Content = null`; fallback renders. Not an error.
- Malformed front matter → file still indexed by its slug; aliases dropped; Warning.
- I/O / permission errors → logged at Warning, degraded to "no doc".
- Path traversal → neutralized by slug rules and the resolved-path guard (unchanged).
- Multiple files claiming one name → first (ordinal) wins; Warning.

## 8. Testing

- **Domain** (`Kafdoc.DomainTest`): `DocumentationPattern.Matches` — `*` spanning dots,
  exact (no-`*`) match, leading/trailing `*`, non-match, empty inputs.
- **Infrastructure** (`Kafdoc.InfrastructureTest`): against a temp directory —
  - direct slug match; pattern (glob) match; own-file precedence over a pattern;
  - aliases parsed from front matter (block list and flow list `[a, b]`);
  - first-match determinism + multi-file overlap Warning;
  - file with no front matter indexes by slug only;
  - malformed front matter degrades to slug-only, no throw, Warning;
  - `HasDocumentation` true via pattern and false when nothing matches;
  - `Rebuild()` picks up a newly added file and swaps the index;
  - `RelativePath` points at the shared file on a pattern match.
- **Application** (`Kafdoc.ApplicationTest`): query services with a substitute
  `IDocumentationStore` — badge driven by `HasDocumentation`; detail DTO carries the
  resolved `DocumentationPath` + content. Update tests that referenced `ListSlugs`.
- **Web** (`Kafdoc.WebTest`, bUnit): content renders and the fallback shows the (own)
  path when absent (unchanged); with `UseYamlFrontMatter()`, a leading `--- … ---`
  block does **not** appear in the rendered HTML.
- **Architecture** (`Kafdoc.ArchitectureTest`): port in Domain, adapter in
  Infrastructure, Web touches only DTOs; existing layer rules still pass (Markdig/
  YamlDotNet are third-party and unconstrained by them).

## Files touched (summary)

- **New:** `Domain/Documentation/DocumentationPattern.cs`;
  `Infrastructure/Documentation/DocumentationIndex.cs`; matching test files.
- **Changed:** `Domain/Documentation/IDocumentationStore.cs` (replace `ListSlugs` with
  `HasDocumentation`); `Infrastructure/Documentation/FileDocumentationStore.cs` (index
  build + Markdig front-matter extraction + `Rebuild`);
  `Application/Services/TopicQueryService.cs`, `UserQueryService.cs`;
  `Web/Program.cs` (add `.UseYamlFrontMatter()`); `Kafdoc.Infrastructure.csproj` (Markdig
  project reference); `Directory.Packages.props` (add YamlDotNet); affected tests in
  `Kafdoc.DomainTest`, `Kafdoc.InfrastructureTest`, `Kafdoc.ApplicationTest`,
  `Kafdoc.WebTest`.
