# Design: Client-side JSON syntax highlighting with Prism.js

**Date:** 2026-06-17
**Status:** Approved, pending implementation

## Problem

Documentation markdown rendered on the topic and user detail pages frequently
contains JSON source inside fenced code blocks (` ```json … ``` `). Today these
blocks render as unstyled monospace text. We want syntax highlighting for them.

## Approach

Markdig already renders a ` ```json ` fence to
`<pre><code class="language-json">…</code></pre>`, which is exactly the markup
convention Prism.js looks for, and the code content is HTML-escaped as Prism
expects. So Prism recognizes our blocks with zero tokenizer configuration.

We therefore use **client-side highlighting with a vendored, globally-loaded
Prism.js**, and re-trigger Prism after each Blazor render so highlighting
survives enhanced navigation. No npm, no build step, fully offline-capable
(matching the existing vendored Bootstrap).

### Why client-side Prism over a server-side Markdig extension

The available server-side syntax-highlighting Markdig extensions are largely
unpopular and several are unmaintained. Prism is widely used, actively
maintained, and its markup convention already matches Markdig's output.

### The one non-obvious problem: when Prism runs

Prism's default trigger is `Prism.highlightAll()` on `DOMContentLoaded`.

- On the **first** page load Blazor server-*prerenders* the HTML, so the
  `<pre><code>` exists when `DOMContentLoaded` fires and Prism highlights it.
- The app uses **InteractiveServer with enhanced navigation**: navigating
  between detail pages *patches* the DOM in place, `DOMContentLoaded` does not
  fire again, and code blocks on subsequently-navigated pages would stay
  unhighlighted.

So we must re-trigger highlighting after Blazor renders. Because Prism is loaded
globally (`window.Prism`), this is a single JS-interop call from the rendering
component — no custom JS module is needed. `Prism.highlightAllUnder` resolves
its Prism instance via closure rather than `this`, so invoking it detached
through interop works correctly.

## Components & files

### 1. Vendored Prism assets — `src/Kafdoc.Web/wwwroot/lib/prism/`
- `prism.css` — light theme (Prism default or `coy`) to match the light UI.
- `prism.js` — Prism **core + markup + json**, minified, built from the Prism
  download page. (markup is tiny and useful since JSON-in-strings is common.)
  No autoloader, since JSON ships directly.

### 2. `App.razor`
Add the stylesheet next to the other `<head>` stylesheets:
```html
<link rel="stylesheet" href="@Assets["lib/prism/prism.css"]" />
```
Add the script at the end of `<body>`, before `blazor.web.js`:
```html
<script src="@Assets["lib/prism/prism.js"]"></script>
```

### 3. `MarkdownContent.razor`
- Inject `IJSRuntime`.
- Put an `ElementReference` on the `.doc-body` element.
- In `OnAfterRenderAsync`, when `Markdown is not null`, call:
  ```csharp
  await JS.InvokeVoidAsync("Prism.highlightAllUnder", _docBodyRef);
  ```
- Wrap the interop in `try/catch (JSException)` so a missing/failed Prism load
  degrades gracefully to plain (unhighlighted) code — the text is already
  correct; highlighting is purely cosmetic.
- Do not invoke for the empty-state branch (no `Markdown`).

## Data flow

```
docs file
  → Markdig.ToHtml (server)
  → <pre><code class="language-json"> in rendered DOM
  → OnAfterRenderAsync
  → JS.InvokeVoidAsync("Prism.highlightAllUnder", docBodyRef)
  → tokenized <span>s styled by prism.css
```

## Error handling

- Interop wrapped in `try/catch (JSException)`; failure leaves readable plain
  code.
- Interop only called after render when the JS runtime is usable, avoiding
  prerender timing issues.

## Testing

- **bUnit** (`Kafdoc.WebTest`): with a substitute `IJSRuntime`,
  - rendering markdown containing a ` ```json ` fence produces
    `<code class="language-json">` in the output;
  - `Prism.highlightAllUnder` is invoked once after rendering non-null markdown;
  - it is **not** invoked for the empty (null `Markdown`) state.
  bUnit has no real JS runtime, so actual visual highlighting is verified
  manually; the unit tests assert the interop contract only.
- No changes to Domain / Application / Infrastructure — those suites are
  untouched.

## Out of scope (YAGNI)

Line-numbers, copy-to-clipboard, dark mode, and languages beyond JSON/markup.
Each is addable later by swapping the vendored `prism.js` / `prism.css`.
