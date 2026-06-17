# Prism JSON Syntax Highlighting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Prism.js client-side syntax highlighting to JSON (and markup) fenced code blocks rendered from documentation markdown on the detail pages.

**Architecture:** Markdig already renders ` ```json ` fences to `<pre><code class="language-json">`, Prism's native convention. We vendor Prism (core + markup + json + a light theme) into `wwwroot/lib/prism`, load it globally in `App.razor`, and re-trigger highlighting after each Blazor render from `MarkdownContent.OnAfterRenderAsync` via a single `IJSRuntime` call, so highlighting survives InteractiveServer enhanced navigation.

**Tech Stack:** Blazor Server (InteractiveServer), Markdig, Prism.js 1.29.0 (vendored static files), bUnit + xUnit v3 + NSubstitute for tests.

## Global Constraints

- Target framework `net10.0`; CI builds with `-warnaserror` — build clean locally with `dotnet build --no-restore -warnaserror`.
- **No git actions.** The user reviews and performs all git operations. Do NOT run `git add`/`git commit`/`git push` in any step.
- Analyzers (Meziantou, SonarAnalyzer, Roslynator) run on build and are gatekeepers; write warning-free code.
- Test runner is Microsoft.Testing.Platform (not VSTest). Run web tests with `dotnet test --no-restore test/Kafdoc.WebTest`.
- Test method names are snake_case: `<MethodName>_<Conditions>_<AssertedOutcome>`, no `Async` suffix; Arrange/Act/Assert with a comment per section.
- XML doc comments on all public members. Prefer primary constructors and auto-properties.
- Vendored assets only — no npm, no libman, no CDN. Mirror the existing vendored Bootstrap under `wwwroot/lib`.

---

### Task 1: Vendor Prism assets and load them globally

**Files:**
- Create: `src/Kafdoc.Web/wwwroot/lib/prism/prism.css`
- Create: `src/Kafdoc.Web/wwwroot/lib/prism/prism.js`
- Modify: `src/Kafdoc.Web/Components/App.razor` (add stylesheet in `<head>`; add script at end of `<body>` before `blazor.web.js`)

**Interfaces:**
- Consumes: nothing.
- Produces: a global `window.Prism` object with the `json` and `markup` languages registered, and a global function path `Prism.highlightAllUnder(element)` callable via JS interop (relied on by Task 2).

- [ ] **Step 1: Download the light theme CSS into the vendor folder**

Run (creates the folder and fetches Prism's default light theme, version 1.29.0):

```bash
mkdir -p src/Kafdoc.Web/wwwroot/lib/prism
curl -fsSL https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/themes/prism.min.css \
  -o src/Kafdoc.Web/wwwroot/lib/prism/prism.css
```

Expected: `prism.css` exists and is non-empty (a few KB).

- [ ] **Step 2: Assemble `prism.js` from core + markup + json**

Run (concatenate the three minified components, in dependency order, into one file):

```bash
cd src/Kafdoc.Web/wwwroot/lib/prism
curl -fsSL https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/components/prism-core.min.js   -o prism-core.min.js
curl -fsSL https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/components/prism-markup.min.js -o prism-markup.min.js
curl -fsSL https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/components/prism-json.min.js   -o prism-json.min.js
cat prism-core.min.js prism-markup.min.js prism-json.min.js > prism.js
rm prism-core.min.js prism-markup.min.js prism-json.min.js
cd -
```

Expected: a single `prism.js` exists; `grep -c "json" src/Kafdoc.Web/wwwroot/lib/prism/prism.js` returns a non-zero count (json grammar is present).

- [ ] **Step 3: Reference the theme stylesheet in `App.razor`**

In `src/Kafdoc.Web/Components/App.razor`, add the Prism stylesheet immediately after the existing app stylesheets in `<head>` (after the `Kafdoc.Web.styles.css` line):

```html
    <link rel="stylesheet" href="@Assets["lib/prism/prism.css"]" />
```

- [ ] **Step 4: Load Prism globally before the Blazor script**

In `src/Kafdoc.Web/Components/App.razor`, add the Prism script at the end of `<body>`, on the line **before** the existing `blazor.web.js` script tag:

```html
    <script src="@Assets["lib/prism/prism.js"]"></script>
```

The resulting end of `<body>` reads:

```html
    <script src="@Assets["lib/prism/prism.js"]"></script>
    <script src="@Assets["_framework/blazor.web.js"]"></script>
```

- [ ] **Step 5: Build to verify the app still compiles and assets resolve**

Run: `dotnet build --no-restore -warnaserror src/Kafdoc.Web`
Expected: build succeeds with no warnings or errors.

- [ ] **Step 6: Manual smoke check (no commit — user handles git)**

Run `cd src/Kafdoc.Web && dotnet run`, open a topic/user detail page whose markdown contains a ` ```json ` block, and confirm the JSON is colorized on first load. (Navigation re-highlighting is added in Task 2.) Do not perform any git actions.

---

### Task 2: Re-trigger Prism after render in MarkdownContent (TDD)

**Files:**
- Modify: `src/Kafdoc.Web/Components/Shared/MarkdownContent.razor`
- Modify (test): `test/Kafdoc.WebTest/MarkdownContentTests.cs`

**Interfaces:**
- Consumes: global `Prism.highlightAllUnder(element)` from Task 1.
- Produces: nothing consumed by later tasks.

**Background for the implementer:**
- bUnit's `BunitContext` defaults to **strict** JSInterop, which throws on any unplanned JS call. Because the component will now call interop during render, the shared `RegisterPipeline()` helper must switch the context to **loose** mode (`JSInterop.Mode = BunitJSInteropMode.Loose;`), which records invocations and returns defaults. This keeps the existing tests passing and lets new tests assert against `JSInterop.Invocations`.
- `Microsoft.JSInterop` is already imported in `_Imports.razor`; `ElementReference` needs no extra using.

- [ ] **Step 1: Switch the test helper to loose JSInterop mode and add the failing highlight test**

In `test/Kafdoc.WebTest/MarkdownContentTests.cs`, change the `RegisterPipeline` helper to also enable loose JSInterop, and add a new test. Replace the existing helper:

```csharp
    private void RegisterPipeline()
    {
        JSInterop.Mode = BunitJSInteropMode.Loose;
        Services.AddSingleton(new MarkdownPipelineBuilder().UseAdvancedExtensions().UseYamlFrontMatter().DisableHtml().Build());
    }
```

Add this test to the class:

```csharp
    [Fact]
    public void Highlights_rendered_markdown_via_prism_after_render()
    {
        // Arrange
        RegisterPipeline();

        // Act
        var cut = Render<MarkdownContent>(ps => ps
            .Add(p => p.Markdown, "```json\n{ \"id\": 42 }\n```")
            .Add(p => p.Path, "topics/orders.md"));

        // Assert — the language class is emitted and Prism was triggered once on the rendered body
        Assert.Contains("language-json", cut.Markup, StringComparison.Ordinal);
        Assert.Single(JSInterop.Invocations, i => i.Identifier == "Prism.highlightAllUnder");
    }
```

- [ ] **Step 2: Run the new test to verify it fails**

Run: `dotnet test --no-restore test/Kafdoc.WebTest --filter-method "*Highlights_rendered_markdown_via_prism_after_render*"`
Expected: FAIL — `JSInterop.Invocations` contains no `Prism.highlightAllUnder` entry (the component does not yet call interop), so `Assert.Single` throws.

- [ ] **Step 3: Implement the interop call in MarkdownContent**

Replace the full contents of `src/Kafdoc.Web/Components/Shared/MarkdownContent.razor` with:

```razor
@inject Markdig.MarkdownPipeline Pipeline
@inject IJSRuntime JS

<div class="documentation">
    @if (Markdown is null)
    {
        <p><em>No additional information available.</em></p>
        <p class="doc-source">Create <code>@Path</code> to add documentation.</p>
    }
    else
    {
        <div class="doc-body" @ref="_docBody">@((MarkupString)Markdig.Markdown.ToHtml(Markdown, Pipeline))</div>
        <p class="doc-source">Source: <code>@Path</code></p>
    }
</div>

@code {
    /// <summary>The rendered documentation body element, highlighted by Prism after each render.</summary>
    private ElementReference _docBody;

    /// <summary>The raw markdown to render, or <c>null</c> when no file exists.</summary>
    [Parameter]
    public string? Markdown { get; set; }

    /// <summary>The expected file path, always shown so authors know what to name a new file.</summary>
    [Parameter]
    [EditorRequired]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Re-applies Prism syntax highlighting to the rendered code blocks after every render so
    /// highlighting survives InteractiveServer enhanced navigation. Highlighting is cosmetic, so a
    /// failed Prism load is swallowed and the plain (escaped) code remains readable.
    /// </summary>
    /// <param name="firstRender">Whether this is the component's first render.</param>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Markdown is null)
        {
            return;
        }

        try
        {
            await JS.InvokeVoidAsync("Prism.highlightAllUnder", _docBody);
        }
        catch (JSException)
        {
            // Highlighting is cosmetic; the escaped code is already readable without it.
        }
    }
}
```

- [ ] **Step 4: Run the new test to verify it passes**

Run: `dotnet test --no-restore test/Kafdoc.WebTest --filter-method "*Highlights_rendered_markdown_via_prism_after_render*"`
Expected: PASS.

- [ ] **Step 5: Add the empty-state test (Prism not triggered when there is no markdown)**

Add this test to `test/Kafdoc.WebTest/MarkdownContentTests.cs`:

```csharp
    [Fact]
    public void Does_not_trigger_prism_when_no_markdown_is_present()
    {
        // Arrange
        RegisterPipeline();

        // Act
        var cut = Render<MarkdownContent>(ps => ps
            .Add(p => p.Markdown, (string?)null)
            .Add(p => p.Path, "users/svc-payments.md"));

        // Assert — the empty-state branch renders no code body, so Prism is never invoked
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier == "Prism.highlightAllUnder");
    }
```

- [ ] **Step 6: Run the full web test project to verify all tests pass**

Run: `dotnet test --no-restore test/Kafdoc.WebTest`
Expected: PASS — the new tests pass and the four pre-existing `MarkdownContentTests` still pass under loose JSInterop mode.

- [ ] **Step 7: Build the whole solution with warnings-as-errors**

Run: `dotnet build --no-restore -warnaserror`
Expected: build succeeds with no warnings or errors.

- [ ] **Step 8: Stop for review (no commit — user handles git)**

Do not perform any git actions. Report completion so the user can review and commit.
