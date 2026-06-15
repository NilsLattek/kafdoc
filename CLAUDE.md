# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Kafdoc documents a single secured Kafka cluster. On startup and on a configurable
interval (default hourly) it reads topics, ACLs, users, and consumer groups via the
Kafka Admin API, builds an in-memory producer/consumer graph (producers from WRITE
ACLs, consumers from READ ACLs + consumer-group offsets, the user↔group bridge from
group-resource READ ACLs), and presents it in a Blazor Server UI (topics list, topic
detail, users). The data lives only in memory; there is no database.

## Commands

```bash
# Fetch nuget packages
dotnet restore
# Then the following commands can all use --no-restore for faster execution

# Build (CI uses -warnaserror, so treat warnings as errors locally too)
dotnet build --no-restore
dotnet build --no-restore -warnaserror

# Run the web app (configure the Kafka section in appsettings / user-secrets first)
cd src/Kafdoc.Web && dotnet run

# Test — runner is Microsoft.Testing.Platform (configured in global.json), not VSTest
dotnet test --no-restore                                                  # all tests
dotnet test --no-restore test/Kafdoc.ApplicationTest                      # one project
dotnet test --no-restore --filter-class "*ClusterGraphBuilderTests*"      # one class
dotnet test --no-restore --filter-method "*Build_maps_write_acl_to_producer_edge*"   # one method

# Integration tests spin up a real secured Kafka broker via Testcontainers and need a
# reachable Docker daemon (provided by the devcontainer's docker-in-docker feature):
dotnet test --no-restore test/Kafdoc.InfrastructureTest
```

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

Do not perform any git actions! I will review the code and perform git actions myself.

## Architecture

This solution follows the domain driven design (DDD) architecture principles.

Four projects forming a dependency chain Web → Application → Domain ← Infrastructure (Domain has no outbound dependencies). The data is a **read model**: Infrastructure fetches raw Kafka facts, a pure domain service derives the graph, Application stores an immutable snapshot in a singleton and exposes query services, and a `BackgroundService` refreshes on a timer.

- **Kafdoc.Domain** — the immutable read model and the derivation core. Raw facts (`RawClusterData`, `RawTopic`/`RawAcl`/`RawConsumerGroup`/`RawScramUser`, Kafka enums) come in through the `IKafkaClusterReader` abstraction (implemented in Infrastructure). `ClusterGraphBuilder` is a **pure** domain service turning `RawClusterData` into a `ClusterGraph` (node records `KafkaTopic`/`KafkaUser`/`KafkaConsumerGroup` and edge records `ProducerEdge`/`ConsumerEdge`/`UserGroupEdge`/`GroupTopicEdge`): producers from `WRITE` ACLs, consumers from `READ` ACLs, the user↔group bridge from group-resource `READ` ACLs, group→topic from committed offsets, with `LITERAL`/`PREFIXED`/`*` pattern expansion. `ClusterSnapshot` wraps a graph with a `CapturedAt` timestamp. This is the most thoroughly unit-tested layer.
- **Kafdoc.Application** — orchestration and the query API. `ISnapshotStore` (thread-safe singleton, `volatile` reference swapped atomically) holds the current snapshot plus status (`LastRefresh`, `LastError`, `IsReady`). `ClusterRefreshService` runs reader → builder → store swap and returns **FluentResults** — exceptions (except `OperationCanceledException`) become `Result.Fail` and leave the previous snapshot intact (stale-but-serving beats empty). `ClusterRefreshHostedService` (`BackgroundService`) refreshes once on startup then on a `PeriodicTimer` at `RefreshOptions.RefreshInterval`. Query services (`ITopicQueryService`/`IUserQueryService`/`ISnapshotStatusService`, `internal` impls) read the singleton snapshot and map to DTOs.
- **Kafdoc.Infrastructure** — the Kafka adapter. `ConfluentKafkaClusterReader : IKafkaClusterReader` fetches and maps over Confluent.Kafka's `IAdminClient` (topics/metadata, `DescribeAclsAsync`, consumer groups + offsets, SCRAM users); it contains **no derivation logic**, keeping the builder broker-free and unit-testable. `KafkaConnectionOptions` is bound from the `Kafka` config section and the `IAdminClient` is built from it.
- **Kafdoc.Web** — Blazor Server (interactive server render mode). Pages read the singleton snapshot directly via the query services: `Topics.razor` (route `/`), `TopicDetail.razor` (`/topics/{Name}`), `Users.razor` (`/users`), plus a `RefreshStatus` component showing the last-refresh time and a manual **Refresh now** button (which resolves the scoped `IClusterRefreshService` from a fresh scope). Because the snapshot singleton is immutable and thread-safe, there is no DbContext and no per-circuit scope dance.

### Dependency injection

Each non-domain project exposes a `Configuration.cs` with a `ConfigureXxx(this IServiceCollection, IConfiguration)` extension method. `Program.cs` calls `ConfigureInfrastructure` / `ConfigureDomain` / `ConfigureApplication` in order. When adding a service, register it in the owning project's `Configuration.cs`, not in `Program.cs`.

## Conventions

- **Central management**: target framework, nullable, analyzers, and `<TargetFramework>net10.0</TargetFramework>` come from `Directory.Build.props`; all package versions are pinned in `Directory.Packages.props` (central package management — add new deps there, version-less `PackageReference` in the csproj).
- **Analyzers as gatekeepers**: Meziantou, SonarAnalyzer, and Roslynator run on build with `EnforceCodeStyleInBuild`. CI builds with `-warnaserror`. Suppress narrowly with `#pragma warning disable <id>` + matching restore when a rule genuinely doesn't apply (see the existing CA1031 catch-all suppression in `ClusterRefreshService` and the CA1822/S2325 static-method suppression on `ClusterGraphBuilder.Build`), rather than disabling globally.
- **Tests**: xUnit v3, **bUnit** for Blazor component tests (`Kafdoc.WebTest`), **NSubstitute** for substitutes, **ArchUnitNET** for layering/naming rules (`Kafdoc.ArchitectureTest`), and **Testcontainers** for the secured-broker integration tests (`Kafdoc.InfrastructureTest`, needs a Docker daemon). Test method names are snake_case describing behavior (`Build_maps_write_acl_to_producer_edge`). `Kafdoc.ApplicationTest` uses substitutes and `FakeTimeProvider` for the refresh/snapshot services.

## Docs

Design spec and implementation plan for the Kafka topic documentation tool live in `docs/superpowers/specs/` and `docs/superpowers/plans/`.

## Model Context Protocol (MCP) Servers

### mslearn

Use the `mslearn` MCP server to find information about latest dotnet / C# features when implementing new features, since we are using the latest dotnet version we should not write old/outdated C# code.

