# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

TODO...

## Commands

```bash
# Fetch nuget packages
dotnet restore
# Then the following commands can all use --no-restore for faster execution

# Build (CI uses -warnaserror, so treat warnings as errors locally too)
dotnet build --no-restore
dotnet build --no-restore -warnaserror

# Run the web app (needs the Postgres db container from the devcontainer)
cd src/Kafdoc.Web && dotnet run

# Test — runner is Microsoft.Testing.Platform (configured in global.json), not VSTest
dotnet test --no-restore                                                  # all tests
dotnet test --no-restore test/Kafdoc.ApplicationTest                     # one project
dotnet test --no-restore --filter-class "*ChartAppServiceTests*"   # one class
dotnet test --no-restore --filter-method "*CreateChartAsync_creates_chart_with_one_root_node*"   # one method
```

### EF Core migrations

Startup project is always `Kafdoc.Web`; migrations live in `Kafdoc.Infrastructure`.

```bash
# Create a new migration
dotnet ef migrations add "Name" -o Data/Migrations \
  --project src/Kafdoc.Infrastructure/Kafdoc.Infrastructure.csproj \
  -s src/Kafdoc.Web/Kafdoc.Web.csproj

# Apply pending migrations
dotnet ef database update \
  --project src/Kafdoc.Infrastructure/Kafdoc.Infrastructure.csproj \
  -s src/Kafdoc.Web/Kafdoc.Web.csproj
```

The devcontainer provides Postgres 18 (`localhost:5432`, user/pass/db all `postgres`); the app container shares the db container's network.

## Code Style

- General:
    - Prefer writing clear code and use inline comments sparingly
- C#: 
    - 4-space indent
    - `PascalCase` for classes/methods
    - `_camelCase` for private fields
    - `camelCase` for local variables, parameters
    - Prefer primary constructors where possible
    - Use auto-properties, and `field` if necessary
    - Write XML comments on all public classes, methods, properties and fields
    - Tests:
        - `<ClassName>Tests` for test class
        - `<MethodName>_<Conditions>_<AssertedOutcome>` for test methods (never `Async` suffix)
        - Arrange, Act, Assert pattern (comment each section in method)

## Versioning

Do not perform any git commit actions on the main branch. Always use feature branches!

## Architecture

This solution follows the domain driven design (DDD) architecture principles.

Four projects forming a dependency chain Web → Application → Domain ← Infrastructure (Domain has no outbound dependencies):

- **Kafdoc.Domain** — entities, domain services, and abstractions only. `OrgNode`/`Chart` are rich entities: private setters, `internal` constructors, a private parameterless ctor for EF, and invariants enforced in the ctor and mutator methods (e.g. `OrgNode.MoveTo`, `Chart` root-node checks). Cross-entity rules that need data access live in domain services (`OrgNodeService`, `ChartService`) — e.g. "root node cannot be moved/deleted", "no moving a node under its own descendant". Data access is abstracted behind `IRepository<T>` (extends Ardalis.Specification's `IRepositoryBase<T>`) and `IUnitOfWork`; query logic lives in `Specifications/` as `*Spec` classes.
- **Kafdoc.Application** — orchestration layer. App services (`IChartAppService`, `IOrgNodeAppService`, `internal` impls) load entities via repositories, invoke domain services, persist, and map to DTOs. They return **FluentResults** `Result`/`Result<T>` — exceptions (except `OperationCanceledException`) are caught and turned into `Result.Fail`; callers branch on `IsFailed`/`Errors`. Entity↔DTO mapping uses **Mapperly** source generators (`Mapper/*Mapper.cs`, `[Mapper]` partial classes).
- **Kafdoc.Infrastructure** — EF Core + Npgsql implementations: `KafdocDbContext`, `EfRepository<T>` (over Ardalis.Specification), `EfUnitOfWork` (wraps work in a transaction). Schema and relationships are configured in `KafdocDbContext.OnModelCreating`; DB naming is snake_case via `EFCore.NamingConventions`.
- **Kafdoc.Web** — Blazor Server (interactive server render mode). The SVG chart is purely presentational: `OrgLayoutService` computes node/connector coordinates (`OrgChartLayout`) from a tree, and components under `Components/Charts/` render boxes/connectors and emit selection/edit events. `ChartWorkspace.razor` (route `/`) is the main page wiring the chart canvas to the editor panel.

### Dependency injection

Each non-domain project exposes a `Configuration.cs` with a `ConfigureXxx(this IServiceCollection, IConfiguration)` extension method. `Program.cs` calls `ConfigureInfrastructure` / `ConfigureDomain` / `ConfigureApplication` in order. When adding a service, register it in the owning project's `Configuration.cs`, not in `Program.cs`.

### Database access from Blazor components

Scoped services (including `KafdocDbContext`) live for the whole SignalR circuit in Blazor Server, so a directly-injected app service would share one long-lived, non-thread-safe `DbContext` for the entire user session — causing concurrent-operation crashes, change-tracker bloat, and stale reads. Therefore **components must not `@inject` the app services** (`IChartAppService`, `IOrgNodeAppService`) directly. Instead inject `IServiceScopeFactory` and run each DB-touching call inside a fresh scope via the `WithNewScopeAsync` extension (`Kafdoc.Web/Extensions/ServiceScopeExtensions.cs`):

```csharp
var result = await ScopeFactory.WithNewScopeAsync(sp =>
    sp.GetRequiredService<IOrgNodeAppService>().UpdateNodeAsync(id, dto, CancellationToken.None));
```

Stateless, non-DB services (e.g. `OrgLayoutService`) may stay directly injected.

## Conventions

- **Central management**: target framework, nullable, analyzers, and `<TargetFramework>net10.0</TargetFramework>` come from `Directory.Build.props`; all package versions are pinned in `Directory.Packages.props` (central package management — add new deps there, version-less `PackageReference` in the csproj).
- **Analyzers as gatekeepers**: Meziantou, SonarAnalyzer, and Roslynator run on build with `EnforceCodeStyleInBuild`. CI builds with `-warnaserror`. Suppress narrowly with `#pragma warning disable <id>` + matching restore when a rule genuinely doesn't apply (see existing EF-ctor and static-method suppressions), rather than disabling globally.
- **Tests**: xUnit v3, **bUnit** for Blazor component tests (`Kafdoc.WebTest`), **NSubstitute** for substitutes. Test method names are snake_case describing behavior (`BuildLayout_stacks_leaf_children_vertically_below_parent`). `Kafdoc.ApplicationTest` uses substitutes — see `TestFactory.CreateUnitOfWorkSubstitute` for the unit-of-work pattern in tests.

## Docs

Design spec and implementation plan for the org-chart tool live in `docs/superpowers/specs/` and `docs/superpowers/plans/`.

## Model Context Protocol (MCP) Servers

### mslearn

Use the `mslearn` MCP server to find information about latest dotnet / C# features when implementing new features, since we are using the latest dotnet version we should not write old/outdated C# code.

