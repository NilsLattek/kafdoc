# Kafka Topic Documentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Connect to a single secured Kafka cluster, read topics/ACLs/users/consumer groups via the Admin API, build an in-memory producer/consumer graph refreshed on startup + hourly, and present it in a Blazor Server UI.

**Architecture:** Four-project DDD layering kept (Web → Application → Domain ← Infrastructure); EF Core/Postgres stripped out. The data is a **read model**: Infrastructure fetches raw Kafka facts behind `IKafkaClusterReader`, a pure `ClusterGraphBuilder` domain service derives the graph, Application stores the immutable snapshot in a singleton and exposes query services, and a `BackgroundService` refreshes on a timer. Producer edges come from `WRITE` ACLs; consumer edges from `READ` ACLs + consumer-group offsets; the user↔group bridge from `READ` ACLs on the group resource.

**Tech Stack:** .NET 10, C#, Blazor Server, Confluent.Kafka (Admin API), FluentResults, Mapperly, xUnit v3 / Microsoft.Testing.Platform, NSubstitute, bUnit, Testcontainers, ArchUnitNET.

**Spec:** `docs/superpowers/specs/2026-06-12-kafka-topic-documentation-design.md`

> **Note on package versions:** Use the `mslearn` MCP server to confirm the latest stable `Confluent.Kafka` and `Testcontainers` versions before pinning them. The versions below are starting points.

> **Build/test commands (from CLAUDE.md):**
> - Build: `dotnet build --no-restore -warnaserror`
> - All tests: `dotnet test --no-restore`
> - One class: `dotnet test --no-restore --filter-class "*ClusterGraphBuilderTests*"`
> - One method: `dotnet test --no-restore --filter-method "*Build_maps_write_acl_to_producer_edge*"`
> CI uses `-warnaserror`, so fix all analyzer warnings as you go.

---

## File Structure

**Domain (`src/Kafdoc.Domain`)**
- `Kafka/IKafkaClusterReader.cs` — abstraction returning raw cluster facts.
- `Kafka/RawClusterData.cs` — raw DTO records + Kafka enums (ACL operation/permission, resource type/pattern).
- `Graph/KafkaTopic.cs`, `Graph/KafkaUser.cs`, `Graph/KafkaConsumerGroup.cs` — node records.
- `Graph/GraphEdges.cs` — edge records (`ProducerEdge`, `ConsumerEdge`, `UserGroupEdge`, `GroupTopicEdge`).
- `Graph/ClusterGraph.cs`, `Graph/ClusterSnapshot.cs` — assembled graph + timestamp.
- `Graph/ClusterGraphBuilder.cs` — pure derivation logic (the testable core).
- `Configuration.cs` — DI registration (replace EF/Topic registration).

**Application (`src/Kafdoc.Application`)**
- `Snapshot/ISnapshotStore.cs`, `Snapshot/SnapshotStore.cs` — singleton holding current snapshot + status.
- `Snapshot/IClusterRefreshService.cs`, `Snapshot/ClusterRefreshService.cs` — orchestrates reader → builder → store swap.
- `Snapshot/ClusterRefreshHostedService.cs` — `BackgroundService` (startup + interval).
- `Snapshot/RefreshOptions.cs` — refresh interval option.
- `Dtos/TopicSummaryDto.cs`, `Dtos/TopicDetailDto.cs`, `Dtos/TopicConsumerDto.cs`, `Dtos/UserSummaryDto.cs`, `Dtos/UserDetailDto.cs`, `Dtos/SnapshotStatusDto.cs`.
- `Services/ITopicQueryService.cs`, `Services/TopicQueryService.cs`, `Services/IUserQueryService.cs`, `Services/UserQueryService.cs`, `Services/ISnapshotStatusService.cs`, `Services/SnapshotStatusService.cs`.
- `Mapper/SnapshotStatusMapper.cs` — Mapperly mapper.
- `Configuration.cs` — DI registration (replace).

**Infrastructure (`src/Kafdoc.Infrastructure`)**
- `Kafka/KafkaConnectionOptions.cs` — connection settings.
- `Kafka/ConfluentKafkaClusterReader.cs` — `IKafkaClusterReader` over `IAdminClient`.
- `Configuration.cs` — DI registration (replace EF with Kafka).

**Web (`src/Kafdoc.Web`)**
- `Components/Pages/Topics.razor` (route `/`), `Components/Pages/TopicDetail.razor` (route `/topics/{Name}`), `Components/Pages/Users.razor` (route `/users`).
- `Components/Layout/RefreshStatus.razor` — last-refresh + Refresh-now button.
- `Components/Layout/NavMenu.razor` — updated nav.
- `Program.cs` — add `public partial class Program;` anchor.

**Tests**
- `test/Kafdoc.DomainTest/Graph/ClusterGraphBuilderTests.cs` — builder unit tests.
- `test/Kafdoc.ApplicationTest/Snapshot/ClusterRefreshServiceTests.cs`, `.../Services/TopicQueryServiceTests.cs`.
- `test/Kafdoc.WebTest/TopicsPageTests.cs`.
- `test/Kafdoc.InfrastructureTest/` (new project) — secured-broker integration tests.
- `test/Kafdoc.ArchitectureTest/*` — updated anchors and rules.

---

## Task 1: Strip EF/Postgres and old placeholder code to a clean build baseline

**Files:**
- Modify: `src/Kafdoc.Infrastructure/Kafdoc.Infrastructure.csproj`, `src/Kafdoc.Domain/Kafdoc.Domain.csproj`, `src/Kafdoc.Web/Kafdoc.Web.csproj`
- Delete: `src/Kafdoc.Domain/IRepository.cs`, `src/Kafdoc.Domain/Entities/Topic.cs`, `src/Kafdoc.Domain/Services/TopicService.cs`, `src/Kafdoc.Domain/Specifications/.gitkeep`, `src/Kafdoc.Infrastructure/Dummy.cs`, `src/Kafdoc.Application/Services/ITopicAppService.cs`, `src/Kafdoc.Application/Services/TopicAppService.cs`, `src/Kafdoc.Application/Dtos/AddTopicDto.cs`, `src/Kafdoc.Web/Extensions/ServiceScopeExtensions.cs`, `src/Kafdoc.Web/Components/Pages/Counter.razor`, `src/Kafdoc.Web/Components/Pages/Weather.razor`
- Delete (old stub tests): `test/Kafdoc.DomainTest/ChartServiceTests.cs`, `test/Kafdoc.ApplicationTest/Services/ChartAppServiceTests.cs`, `test/Kafdoc.WebTest/OrgNodeBoxTests.cs`
- Modify: `src/Kafdoc.Application/Mapper/ExampleMapper.cs`, `src/Kafdoc.Web/Program.cs`, `src/Kafdoc.Web/appsettings.Development.json`, `Directory.Packages.props`

- [x] **Step 1: Remove EF/Ardalis packages from `Directory.Packages.props`**

Delete these `<PackageVersion>` lines:

```xml
<PackageVersion Include="Ardalis.Specification" Version="9.3.1" />
<PackageVersion Include="Ardalis.Specification.EntityFrameworkCore" Version="9.3.1" />
<PackageVersion Include="EFCore.NamingConventions" Version="10.0.1" />
<PackageVersion Include="EntityFrameworkCore.Exceptions.PostgreSQL" Version="10.0.1" />
<PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.9" />
<PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.2" />
```

Add these new `<PackageVersion>` lines (verify latest via mslearn):

```xml
<PackageVersion Include="Confluent.Kafka" Version="2.6.1" />
<PackageVersion Include="Microsoft.Extensions.Hosting.Abstractions" Version="10.0.9" />
<PackageVersion Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.9" />
<PackageVersion Include="Testcontainers" Version="4.1.0" />
```

- [x] **Step 2: Rewrite `src/Kafdoc.Infrastructure/Kafdoc.Infrastructure.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="Confluent.Kafka" />
    <PackageReference Include="Microsoft.Extensions.Configuration" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../Kafdoc.Domain/Kafdoc.Domain.csproj" />
  </ItemGroup>

</Project>
```

- [x] **Step 3: Rewrite `src/Kafdoc.Domain/Kafdoc.Domain.csproj`** (drop Ardalis)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Configuration" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Kafdoc.Domain.Common/Kafdoc.Domain.Common.csproj" />
  </ItemGroup>
</Project>
```

- [x] **Step 4: Rewrite `src/Kafdoc.Web/Kafdoc.Web.csproj`** (drop EF design)

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <BlazorDisableThrowNavigationException>true</BlazorDisableThrowNavigationException>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../Kafdoc.Application/Kafdoc.Application.csproj" />
    <ProjectReference Include="../Kafdoc.Infrastructure/Kafdoc.Infrastructure.csproj" />
  </ItemGroup>

</Project>
```

- [x] **Step 5: Add Hosting/Options packages to `src/Kafdoc.Application/Kafdoc.Application.csproj`**

Add inside the existing `<ItemGroup>` of `<PackageReference>`s:

```xml
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" />
```

- [x] **Step 6: Delete obsolete source and test files**

```bash
git rm src/Kafdoc.Domain/IRepository.cs \
       src/Kafdoc.Domain/Entities/Topic.cs \
       src/Kafdoc.Domain/Services/TopicService.cs \
       src/Kafdoc.Domain/Specifications/.gitkeep \
       src/Kafdoc.Infrastructure/Dummy.cs \
       src/Kafdoc.Application/Services/ITopicAppService.cs \
       src/Kafdoc.Application/Services/TopicAppService.cs \
       src/Kafdoc.Application/Dtos/AddTopicDto.cs \
       src/Kafdoc.Web/Extensions/ServiceScopeExtensions.cs \
       src/Kafdoc.Web/Components/Pages/Counter.razor \
       src/Kafdoc.Web/Components/Pages/Weather.razor \
       test/Kafdoc.DomainTest/ChartServiceTests.cs \
       test/Kafdoc.ApplicationTest/Services/ChartAppServiceTests.cs \
       test/Kafdoc.WebTest/OrgNodeBoxTests.cs
```

- [x] **Step 7: Replace `src/Kafdoc.Application/Mapper/ExampleMapper.cs`** with an empty placeholder (real mapper added in Task 5)

Delete the file for now:

```bash
git rm src/Kafdoc.Application/Mapper/ExampleMapper.cs
```

- [x] **Step 8: Replace `src/Kafdoc.Domain/Configuration.cs`** (remove `TopicService` registration; leave an empty body the later tasks extend)

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kafdoc.Domain;

/// <summary>
/// Dependency injection registrations for the Domain layer.
/// </summary>
public static class Configuration
{
    /// <summary>
    /// Registers Domain-layer services.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">The application configuration.</param>
    public static void ConfigureDomain(this IServiceCollection services, IConfiguration configuration)
    {
        // Graph builder is registered in Task 4.
    }
}
```

- [x] **Step 9: Replace `src/Kafdoc.Application/Configuration.cs`** (remove `ITopicAppService` registration)

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kafdoc.Application;

/// <summary>
/// Dependency injection registrations for the Application layer.
/// </summary>
public static class Configuration
{
    /// <summary>
    /// Registers Application-layer services.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">The application configuration.</param>
    public static void ConfigureApplication(this IServiceCollection services, IConfiguration configuration)
    {
        // Snapshot, refresh, and query services are registered in Tasks 5 and 6.
    }
}
```

- [x] **Step 9b: Replace `src/Kafdoc.Infrastructure/Configuration.cs`** with an EF-free placeholder (the EF `using` would otherwise break the build until Task 7)

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kafdoc.Infrastructure;

/// <summary>
/// Dependency injection registrations for the Infrastructure layer.
/// </summary>
public static class Configuration
{
    /// <summary>
    /// Registers Infrastructure-layer services.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">The application configuration.</param>
    public static void ConfigureInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Kafka admin client and reader are registered in Task 7.
    }
}
```

- [x] **Step 9c: Remove the dead `@using Kafdoc.Web.Extensions` from `src/Kafdoc.Web/Components/_Imports.razor`**

Delete this line (the `Extensions` namespace is now empty after deleting `ServiceScopeExtensions.cs`, so the directive would fail to compile):

```razor
@using Kafdoc.Web.Extensions
```

- [x] **Step 10: Append `public partial class Program;` to `src/Kafdoc.Web/Program.cs`** (stable assembly anchor for architecture tests)

Add as the last line of the file:

```csharp

/// <summary>Exposes the implicit entry-point class so tests can anchor to the Web assembly.</summary>
public partial class Program;
```

- [x] **Step 11: Clean `src/Kafdoc.Web/appsettings.Development.json`** (remove EF/Postgres settings)

```json
{
  "DetailedErrors": true,
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.AspNetCore.SignalR": "Information"
    }
  }
}
```

- [x] **Step 12: Update `test/Kafdoc.ArchitectureTest/ArchitectureModel.cs` anchors**

Replace the three anchor lines that reference removed types:

```csharp
    private static readonly System.Reflection.Assembly DomainAssembly =
        typeof(Kafdoc.Domain.Kafka.IKafkaClusterReader).Assembly;
```
```csharp
    private static readonly System.Reflection.Assembly ApplicationAssembly =
        typeof(Kafdoc.Application.Services.ITopicQueryService).Assembly;
```
```csharp
    private static readonly System.Reflection.Assembly InfrastructureAssembly =
        typeof(Kafdoc.Infrastructure.ConfluentKafkaClusterReader).Assembly;
```
```csharp
    private static readonly System.Reflection.Assembly WebAssembly =
        typeof(Program).Assembly;
```

> These anchor types are created in Tasks 2, 5, 7. The architecture-test project will not compile until those tasks are done — that is expected; this task's build verification (Step 14) covers `src/` only.

- [x] **Step 13: Update architecture test rules for the new layering**

In `test/Kafdoc.ArchitectureTest/NamingConventionTests.cs`, delete the `Specifications_end_with_spec_suffix` test method entirely (no `Specifications` namespace remains). Keep `Mappers_end_with_mapper_suffix`.

In `test/Kafdoc.ArchitectureTest/ApplicationServiceTests.cs`, broaden the rule to all service implementations and fix the XML summary:

```csharp
    [Fact]
    public void Service_implementations_are_not_public()
    {
        // Arrange
        IArchRule rule = Classes().That()
            .ResideInNamespace("Kafdoc.Application.Services")
            .And().HaveNameEndingWith("Service")
            .Should().NotBePublic();

        // Act + Assert
        rule.Check(Architecture);
    }
```

- [x] **Step 14: Build `src/` to verify the baseline compiles**

Run: `dotnet build --no-restore src/Kafdoc.Web/Kafdoc.Web.csproj -warnaserror`
Expected: PASS (no warnings). The architecture test project is intentionally not built here.

- [x] **Step 15: Commit**

```bash
git add -A
git commit -m "chore: strip EF/Postgres stack and old placeholder code"
```

---

## Task 2: Domain — raw cluster facts and the reader abstraction

**Files:**
- Create: `src/Kafdoc.Domain/Kafka/RawClusterData.cs`, `src/Kafdoc.Domain/Kafka/IKafkaClusterReader.cs`

- [x] **Step 1: Create `src/Kafdoc.Domain/Kafka/RawClusterData.cs`**

```csharp
namespace Kafdoc.Domain.Kafka;

/// <summary>Kafka ACL operations relevant to the producer/consumer graph.</summary>
public enum KafkaAclOperation
{
    /// <summary>Any operation not separately modelled.</summary>
    Other = 0,
    /// <summary>Permission to read (consume) from a resource.</summary>
    Read,
    /// <summary>Permission to write (produce) to a resource.</summary>
    Write,
    /// <summary>Permission covering all operations.</summary>
    All,
}

/// <summary>Whether an ACL grants or denies access.</summary>
public enum KafkaAclPermission
{
    /// <summary>Permission type not recognised.</summary>
    Other = 0,
    /// <summary>Access is allowed.</summary>
    Allow,
    /// <summary>Access is denied.</summary>
    Deny,
}

/// <summary>The type of resource an ACL applies to.</summary>
public enum KafkaResourceType
{
    /// <summary>A resource type not separately modelled.</summary>
    Other = 0,
    /// <summary>A topic resource.</summary>
    Topic,
    /// <summary>A consumer group resource.</summary>
    Group,
    /// <summary>The cluster resource.</summary>
    Cluster,
    /// <summary>A transactional id resource.</summary>
    TransactionalId,
}

/// <summary>How an ACL resource name should be matched.</summary>
public enum KafkaResourcePatternType
{
    /// <summary>A pattern type not separately modelled.</summary>
    Other = 0,
    /// <summary>The name matches a single resource exactly.</summary>
    Literal,
    /// <summary>The name is a prefix matching any resource starting with it.</summary>
    Prefixed,
}

/// <summary>A single ACL binding as read from the cluster.</summary>
/// <param name="Principal">The principal the ACL applies to, e.g. <c>User:svc-payments</c>.</param>
/// <param name="ResourceType">The resource type the ACL targets.</param>
/// <param name="ResourceName">The resource name or prefix.</param>
/// <param name="PatternType">How <paramref name="ResourceName"/> is matched.</param>
/// <param name="Operation">The operation granted or denied.</param>
/// <param name="Permission">Whether the operation is allowed or denied.</param>
public sealed record RawAcl(
    string Principal,
    KafkaResourceType ResourceType,
    string ResourceName,
    KafkaResourcePatternType PatternType,
    KafkaAclOperation Operation,
    KafkaAclPermission Permission);

/// <summary>A topic as read from cluster metadata.</summary>
/// <param name="Name">The topic name.</param>
/// <param name="PartitionCount">The number of partitions.</param>
public sealed record RawTopic(string Name, int PartitionCount);

/// <summary>A consumer group as read from the cluster.</summary>
/// <param name="GroupId">The group id.</param>
/// <param name="State">The group state, e.g. <c>Stable</c> or <c>Empty</c>.</param>
/// <param name="MemberCount">The number of active members.</param>
/// <param name="ConsumedTopics">The distinct topics the group has committed offsets for.</param>
public sealed record RawConsumerGroup(
    string GroupId,
    string State,
    int MemberCount,
    IReadOnlyList<string> ConsumedTopics);

/// <summary>A user with SCRAM credentials declared on the cluster.</summary>
/// <param name="Principal">The principal, e.g. <c>User:svc-payments</c>.</param>
public sealed record RawScramUser(string Principal);

/// <summary>The complete set of raw facts read from a Kafka cluster in one pass.</summary>
/// <param name="Topics">All topics and their partition counts.</param>
/// <param name="Acls">All ACL bindings.</param>
/// <param name="ConsumerGroups">All consumer groups with their consumed topics.</param>
/// <param name="ScramUsers">All users that have SCRAM credentials.</param>
public sealed record RawClusterData(
    IReadOnlyList<RawTopic> Topics,
    IReadOnlyList<RawAcl> Acls,
    IReadOnlyList<RawConsumerGroup> ConsumerGroups,
    IReadOnlyList<RawScramUser> ScramUsers);
```

- [x] **Step 2: Create `src/Kafdoc.Domain/Kafka/IKafkaClusterReader.cs`**

```csharp
namespace Kafdoc.Domain.Kafka;

/// <summary>
/// Reads the raw facts (topics, ACLs, consumer groups, users) from a Kafka cluster.
/// Implementations live in the Infrastructure layer.
/// </summary>
public interface IKafkaClusterReader
{
    /// <summary>
    /// Reads the current topics, ACLs, consumer groups, and SCRAM users from the cluster.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The raw cluster data.</returns>
    Task<RawClusterData> ReadAsync(CancellationToken cancellationToken);
}
```

- [x] **Step 3: Build the Domain project**

Run: `dotnet build --no-restore src/Kafdoc.Domain/Kafdoc.Domain.csproj -warnaserror`
Expected: PASS.

- [x] **Step 4: Commit**

```bash
git add src/Kafdoc.Domain/Kafka
git commit -m "feat(domain): add raw Kafka facts and reader abstraction"
```

---

## Task 3: Domain — graph nodes, edges, and snapshot

**Files:**
- Create: `src/Kafdoc.Domain/Graph/KafkaTopic.cs`, `KafkaUser.cs`, `KafkaConsumerGroup.cs`, `GraphEdges.cs`, `ClusterGraph.cs`, `ClusterSnapshot.cs`

- [x] **Step 1: Create the node records**

`src/Kafdoc.Domain/Graph/KafkaTopic.cs`:
```csharp
namespace Kafdoc.Domain.Graph;

/// <summary>A topic node in the cluster graph.</summary>
/// <param name="Name">The topic name.</param>
/// <param name="PartitionCount">The number of partitions.</param>
public sealed record KafkaTopic(string Name, int PartitionCount);
```

`src/Kafdoc.Domain/Graph/KafkaUser.cs`:
```csharp
namespace Kafdoc.Domain.Graph;

/// <summary>A user (principal) node in the cluster graph.</summary>
/// <param name="Principal">The principal, e.g. <c>User:svc-payments</c>.</param>
/// <param name="HasScramCredentials">Whether the principal has SCRAM credentials on the cluster.</param>
public sealed record KafkaUser(string Principal, bool HasScramCredentials);
```

`src/Kafdoc.Domain/Graph/KafkaConsumerGroup.cs`:
```csharp
namespace Kafdoc.Domain.Graph;

/// <summary>A consumer group node in the cluster graph.</summary>
/// <param name="GroupId">The group id.</param>
/// <param name="State">The group state, e.g. <c>Stable</c> or <c>Empty</c>.</param>
/// <param name="MemberCount">The number of active members.</param>
public sealed record KafkaConsumerGroup(string GroupId, string State, int MemberCount);
```

- [x] **Step 2: Create `src/Kafdoc.Domain/Graph/GraphEdges.cs`**

```csharp
namespace Kafdoc.Domain.Graph;

/// <summary>A user is permitted to produce to a topic (from a <c>WRITE</c> ACL).</summary>
/// <param name="Principal">The producing principal.</param>
/// <param name="Topic">The target topic name.</param>
public sealed record ProducerEdge(string Principal, string Topic);

/// <summary>A user is permitted to consume a topic (from a <c>READ</c> ACL on the topic).</summary>
/// <param name="Principal">The consuming principal.</param>
/// <param name="Topic">The target topic name.</param>
public sealed record ConsumerEdge(string Principal, string Topic);

/// <summary>A user backs a consumer group (from a <c>READ</c> ACL on the group resource).</summary>
/// <param name="Principal">The principal that owns/uses the group.</param>
/// <param name="GroupId">The consumer group id.</param>
public sealed record UserGroupEdge(string Principal, string GroupId);

/// <summary>A consumer group actually consumes a topic (from committed offsets).</summary>
/// <param name="GroupId">The consumer group id.</param>
/// <param name="Topic">The consumed topic name.</param>
public sealed record GroupTopicEdge(string GroupId, string Topic);
```

- [x] **Step 3: Create `src/Kafdoc.Domain/Graph/ClusterGraph.cs`**

```csharp
namespace Kafdoc.Domain.Graph;

/// <summary>
/// The assembled producer/consumer graph: typed nodes and the edges derived from
/// ACLs and consumer-group metadata.
/// </summary>
/// <param name="Topics">Topic nodes.</param>
/// <param name="Users">User (principal) nodes.</param>
/// <param name="ConsumerGroups">Consumer group nodes.</param>
/// <param name="Producers">User → topic produce edges (from WRITE ACLs).</param>
/// <param name="Consumers">User → topic consume edges (from READ ACLs on topics).</param>
/// <param name="UserGroups">User → group edges (from READ ACLs on group resources).</param>
/// <param name="GroupConsumption">Group → topic edges (from committed offsets).</param>
public sealed record ClusterGraph(
    IReadOnlyList<KafkaTopic> Topics,
    IReadOnlyList<KafkaUser> Users,
    IReadOnlyList<KafkaConsumerGroup> ConsumerGroups,
    IReadOnlyList<ProducerEdge> Producers,
    IReadOnlyList<ConsumerEdge> Consumers,
    IReadOnlyList<UserGroupEdge> UserGroups,
    IReadOnlyList<GroupTopicEdge> GroupConsumption);
```

- [x] **Step 4: Create `src/Kafdoc.Domain/Graph/ClusterSnapshot.cs`**

```csharp
namespace Kafdoc.Domain.Graph;

/// <summary>A cluster graph captured at a point in time.</summary>
/// <param name="Graph">The producer/consumer graph.</param>
/// <param name="CapturedAt">When the graph was captured.</param>
public sealed record ClusterSnapshot(ClusterGraph Graph, DateTimeOffset CapturedAt);
```

- [x] **Step 5: Build**

Run: `dotnet build --no-restore src/Kafdoc.Domain/Kafdoc.Domain.csproj -warnaserror`
Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add src/Kafdoc.Domain/Graph
git commit -m "feat(domain): add cluster graph node/edge/snapshot records"
```

---

## Task 4: Domain — `ClusterGraphBuilder` (pure derivation, TDD)

This is the core logic. Build it test-first. The builder turns `RawClusterData` into a `ClusterGraph`, applying these rules:
- Users = union of all ACL principals + all SCRAM users (SCRAM flag set when present).
- Producer edge per `Allow`+`WRITE` ACL on a topic resource, expanded over matching topics (LITERAL = exact, PREFIXED = startsWith).
- Consumer edge per `Allow`+`READ` ACL on a topic resource, expanded the same way.
- UserGroup edge per `Allow`+`READ` ACL on a group resource, expanded over matching groups.
- GroupTopic edge per group's consumed topic.
- `Deny` ACLs and non-topic/group resources are ignored for edges (still contribute the principal as a user).
- Edges are de-duplicated.

**Files:**
- Create: `src/Kafdoc.Domain/Graph/ClusterGraphBuilder.cs`
- Test: `test/Kafdoc.DomainTest/Graph/ClusterGraphBuilderTests.cs`

- [x] **Step 1: Add a Domain project reference to `Kafdoc.Domain.csproj`?** — not needed; `Kafdoc.DomainTest` already references Domain. Confirm by opening `test/Kafdoc.DomainTest/Kafdoc.DomainTest.csproj` (already references `../../src/Kafdoc.Domain/Kafdoc.Domain.csproj`). No change.

- [x] **Step 2: Write the failing test file `test/Kafdoc.DomainTest/Graph/ClusterGraphBuilderTests.cs`**

```csharp
using Kafdoc.Domain.Graph;
using Kafdoc.Domain.Kafka;

namespace Kafdoc.DomainTest.Graph;

public class ClusterGraphBuilderTests
{
    private static RawClusterData Raw(
        IReadOnlyList<RawTopic>? topics = null,
        IReadOnlyList<RawAcl>? acls = null,
        IReadOnlyList<RawConsumerGroup>? groups = null,
        IReadOnlyList<RawScramUser>? scram = null) =>
        new(topics ?? [], acls ?? [], groups ?? [], scram ?? []);

    private static RawAcl Acl(
        string principal,
        KafkaResourceType type,
        string name,
        KafkaAclOperation op,
        KafkaResourcePatternType pattern = KafkaResourcePatternType.Literal,
        KafkaAclPermission permission = KafkaAclPermission.Allow) =>
        new(principal, type, name, pattern, op, permission);

    [Fact]
    public void Build_maps_write_acl_to_producer_edge()
    {
        // Arrange
        var builder = new ClusterGraphBuilder();
        var raw = Raw(
            topics: [new RawTopic("orders", 3)],
            acls: [Acl("User:svc-orders", KafkaResourceType.Topic, "orders", KafkaAclOperation.Write)]);

        // Act
        var graph = builder.Build(raw);

        // Assert
        Assert.Contains(new ProducerEdge("User:svc-orders", "orders"), graph.Producers);
        Assert.Empty(graph.Consumers);
    }

    [Fact]
    public void Build_maps_read_acl_on_topic_to_consumer_edge()
    {
        // Arrange
        var builder = new ClusterGraphBuilder();
        var raw = Raw(
            topics: [new RawTopic("orders", 3)],
            acls: [Acl("User:svc-billing", KafkaResourceType.Topic, "orders", KafkaAclOperation.Read)]);

        // Act
        var graph = builder.Build(raw);

        // Assert
        Assert.Contains(new ConsumerEdge("User:svc-billing", "orders"), graph.Consumers);
        Assert.Empty(graph.Producers);
    }

    [Fact]
    public void Build_maps_read_acl_on_group_to_user_group_edge()
    {
        // Arrange
        var builder = new ClusterGraphBuilder();
        var raw = Raw(
            groups: [new RawConsumerGroup("billing-app", "Stable", 2, [])],
            acls: [Acl("User:svc-billing", KafkaResourceType.Group, "billing-app", KafkaAclOperation.Read)]);

        // Act
        var graph = builder.Build(raw);

        // Assert
        Assert.Contains(new UserGroupEdge("User:svc-billing", "billing-app"), graph.UserGroups);
    }

    [Fact]
    public void Build_maps_group_committed_offsets_to_group_topic_edges()
    {
        // Arrange
        var builder = new ClusterGraphBuilder();
        var raw = Raw(
            topics: [new RawTopic("orders", 3)],
            groups: [new RawConsumerGroup("billing-app", "Stable", 2, ["orders"])]);

        // Act
        var graph = builder.Build(raw);

        // Assert
        Assert.Contains(new GroupTopicEdge("billing-app", "orders"), graph.GroupConsumption);
    }

    [Fact]
    public void Build_expands_prefixed_topic_acl_over_matching_topics()
    {
        // Arrange
        var builder = new ClusterGraphBuilder();
        var raw = Raw(
            topics: [new RawTopic("orders.created", 1), new RawTopic("orders.shipped", 1), new RawTopic("billing.paid", 1)],
            acls: [Acl("User:svc-orders", KafkaResourceType.Topic, "orders.", KafkaAclOperation.Write, KafkaResourcePatternType.Prefixed)]);

        // Act
        var graph = builder.Build(raw);

        // Assert
        Assert.Contains(new ProducerEdge("User:svc-orders", "orders.created"), graph.Producers);
        Assert.Contains(new ProducerEdge("User:svc-orders", "orders.shipped"), graph.Producers);
        Assert.DoesNotContain(new ProducerEdge("User:svc-orders", "billing.paid"), graph.Producers);
    }

    [Fact]
    public void Build_treats_wildcard_literal_acl_as_all_topics()
    {
        // Arrange
        var builder = new ClusterGraphBuilder();
        var raw = Raw(
            topics: [new RawTopic("a", 1), new RawTopic("b", 1)],
            acls: [Acl("User:admin", KafkaResourceType.Topic, "*", KafkaAclOperation.Read)]);

        // Act
        var graph = builder.Build(raw);

        // Assert
        Assert.Contains(new ConsumerEdge("User:admin", "a"), graph.Consumers);
        Assert.Contains(new ConsumerEdge("User:admin", "b"), graph.Consumers);
    }

    [Fact]
    public void Build_includes_principals_without_scram_as_users()
    {
        // Arrange
        var builder = new ClusterGraphBuilder();
        var raw = Raw(
            topics: [new RawTopic("orders", 1)],
            acls: [Acl("User:mtls-client", KafkaResourceType.Topic, "orders", KafkaAclOperation.Read)],
            scram: [new RawScramUser("User:svc-billing")]);

        // Act
        var graph = builder.Build(raw);

        // Assert
        Assert.Contains(graph.Users, u => u.Principal == "User:mtls-client" && !u.HasScramCredentials);
        Assert.Contains(graph.Users, u => u.Principal == "User:svc-billing" && u.HasScramCredentials);
    }

    [Fact]
    public void Build_ignores_deny_acls_for_edges()
    {
        // Arrange
        var builder = new ClusterGraphBuilder();
        var raw = Raw(
            topics: [new RawTopic("orders", 1)],
            acls: [Acl("User:blocked", KafkaResourceType.Topic, "orders", KafkaAclOperation.Write, permission: KafkaAclPermission.Deny)]);

        // Act
        var graph = builder.Build(raw);

        // Assert
        Assert.Empty(graph.Producers);
        Assert.Contains(graph.Users, u => u.Principal == "User:blocked");
    }

    [Fact]
    public void Build_deduplicates_overlapping_edges()
    {
        // Arrange — an All ACL and a Write ACL both imply produce
        var builder = new ClusterGraphBuilder();
        var raw = Raw(
            topics: [new RawTopic("orders", 1)],
            acls:
            [
                Acl("User:svc", KafkaResourceType.Topic, "orders", KafkaAclOperation.Write),
                Acl("User:svc", KafkaResourceType.Topic, "orders", KafkaAclOperation.All),
            ]);

        // Act
        var graph = builder.Build(raw);

        // Assert
        Assert.Single(graph.Producers, e => e == new ProducerEdge("User:svc", "orders"));
    }
}
```

- [x] **Step 3: Run the tests to verify they fail to compile (builder missing)**

Run: `dotnet test --no-restore test/Kafdoc.DomainTest --filter-class "*ClusterGraphBuilderTests*"`
Expected: FAIL — `ClusterGraphBuilder` does not exist.

- [x] **Step 4: Implement `src/Kafdoc.Domain/Graph/ClusterGraphBuilder.cs`**

```csharp
using Kafdoc.Domain.Kafka;

namespace Kafdoc.Domain.Graph;

/// <summary>
/// Builds a <see cref="ClusterGraph"/> from raw Kafka facts. Pure and deterministic:
/// it performs no I/O and depends only on its input.
/// </summary>
public sealed class ClusterGraphBuilder
{
    /// <summary>
    /// Derives the producer/consumer graph from raw cluster data.
    /// </summary>
    /// <param name="raw">The raw topics, ACLs, consumer groups, and SCRAM users.</param>
    /// <returns>The assembled graph.</returns>
    public ClusterGraph Build(RawClusterData raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var topics = raw.Topics
            .Select(t => new KafkaTopic(t.Name, t.PartitionCount))
            .ToList();

        var groups = raw.ConsumerGroups
            .Select(g => new KafkaConsumerGroup(g.GroupId, g.State, g.MemberCount))
            .ToList();

        var topicNames = topics.Select(t => t.Name).ToList();
        var groupIds = groups.Select(g => g.GroupId).ToList();

        var allowAcls = raw.Acls.Where(a => a.Permission == KafkaAclPermission.Allow).ToList();

        var producers = new HashSet<ProducerEdge>();
        var consumers = new HashSet<ConsumerEdge>();
        var userGroups = new HashSet<UserGroupEdge>();

        foreach (var acl in allowAcls)
        {
            if (acl.ResourceType == KafkaResourceType.Topic)
            {
                foreach (var topic in MatchResources(acl, topicNames))
                {
                    if (acl.Operation is KafkaAclOperation.Write or KafkaAclOperation.All)
                    {
                        producers.Add(new ProducerEdge(acl.Principal, topic));
                    }

                    if (acl.Operation is KafkaAclOperation.Read or KafkaAclOperation.All)
                    {
                        consumers.Add(new ConsumerEdge(acl.Principal, topic));
                    }
                }
            }
            else if (acl.ResourceType == KafkaResourceType.Group
                && acl.Operation is KafkaAclOperation.Read or KafkaAclOperation.All)
            {
                foreach (var groupId in MatchResources(acl, groupIds))
                {
                    userGroups.Add(new UserGroupEdge(acl.Principal, groupId));
                }
            }
        }

        var groupConsumption = raw.ConsumerGroups
            .SelectMany(g => g.ConsumedTopics.Select(t => new GroupTopicEdge(g.GroupId, t)))
            .Distinct()
            .ToList();

        var scramPrincipals = raw.ScramUsers.Select(u => u.Principal).ToHashSet(StringComparer.Ordinal);
        var users = raw.Acls.Select(a => a.Principal)
            .Concat(scramPrincipals)
            .Distinct(StringComparer.Ordinal)
            .Select(p => new KafkaUser(p, scramPrincipals.Contains(p)))
            .ToList();

        return new ClusterGraph(
            topics,
            users,
            groups,
            [.. producers],
            [.. consumers],
            [.. userGroups],
            groupConsumption);
    }

    private static IEnumerable<string> MatchResources(RawAcl acl, IReadOnlyList<string> candidates)
    {
        if (acl.PatternType == KafkaResourcePatternType.Prefixed)
        {
            return candidates.Where(c => c.StartsWith(acl.ResourceName, StringComparison.Ordinal));
        }

        // Literal: "*" is the Kafka wildcard meaning all resources; otherwise an exact match.
        if (acl.ResourceName == "*")
        {
            return candidates;
        }

        return candidates.Where(c => string.Equals(c, acl.ResourceName, StringComparison.Ordinal));
    }
}
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --no-restore test/Kafdoc.DomainTest --filter-class "*ClusterGraphBuilderTests*"`
Expected: PASS (all 9 tests).

- [x] **Step 6: Register the builder in `src/Kafdoc.Domain/Configuration.cs`**

Replace the comment in `ConfigureDomain` with:

```csharp
        services.AddSingleton<Kafdoc.Domain.Graph.ClusterGraphBuilder>();
```

- [x] **Step 7: Build and commit**

Run: `dotnet build --no-restore src/Kafdoc.Domain/Kafdoc.Domain.csproj -warnaserror`
Expected: PASS.

```bash
git add src/Kafdoc.Domain/Graph/ClusterGraphBuilder.cs src/Kafdoc.Domain/Configuration.cs test/Kafdoc.DomainTest/Graph
git commit -m "feat(domain): implement ClusterGraphBuilder with unit tests"
```

---

## Task 5: Application — snapshot store, refresh service, DTOs, query services

**Files:**
- Create: `src/Kafdoc.Application/Snapshot/ISnapshotStore.cs`, `SnapshotStore.cs`, `IClusterRefreshService.cs`, `ClusterRefreshService.cs`, `RefreshOptions.cs`
- Create: `src/Kafdoc.Application/Dtos/TopicSummaryDto.cs`, `TopicDetailDto.cs`, `TopicConsumerDto.cs`, `UserSummaryDto.cs`, `UserDetailDto.cs`, `SnapshotStatusDto.cs`
- Create: `src/Kafdoc.Application/Services/ITopicQueryService.cs`, `TopicQueryService.cs`, `IUserQueryService.cs`, `UserQueryService.cs`, `ISnapshotStatusService.cs`, `SnapshotStatusService.cs`
- Test: `test/Kafdoc.ApplicationTest/Snapshot/ClusterRefreshServiceTests.cs`, `test/Kafdoc.ApplicationTest/Services/TopicQueryServiceTests.cs`

- [x] **Step 1: Create `src/Kafdoc.Application/Snapshot/ISnapshotStore.cs`**

```csharp
using Kafdoc.Domain.Graph;

namespace Kafdoc.Application.Snapshot;

/// <summary>
/// Holds the current in-memory cluster snapshot and the status of the last refresh.
/// Implementations are thread-safe singletons; reads are lock-free.
/// </summary>
public interface ISnapshotStore
{
    /// <summary>The most recently captured snapshot, or <see langword="null"/> before the first refresh.</summary>
    ClusterSnapshot? Current { get; }

    /// <summary>When the last successful refresh completed, or <see langword="null"/> if none yet.</summary>
    DateTimeOffset? LastRefresh { get; }

    /// <summary>The error message from the last failed refresh, or <see langword="null"/> if the last refresh succeeded.</summary>
    string? LastError { get; }

    /// <summary>Whether at least one snapshot has been captured.</summary>
    bool IsReady { get; }

    /// <summary>Atomically replaces the current snapshot and clears the last error.</summary>
    /// <param name="snapshot">The new snapshot.</param>
    void SetSnapshot(ClusterSnapshot snapshot);

    /// <summary>Records that the last refresh failed, leaving the existing snapshot in place.</summary>
    /// <param name="error">The error message.</param>
    void SetError(string error);
}
```

- [x] **Step 2: Create `src/Kafdoc.Application/Snapshot/SnapshotStore.cs`**

```csharp
using Kafdoc.Domain.Graph;

namespace Kafdoc.Application.Snapshot;

/// <summary>Thread-safe singleton implementation of <see cref="ISnapshotStore"/>.</summary>
internal sealed class SnapshotStore : ISnapshotStore
{
    private volatile ClusterSnapshot? _current;
    private volatile string? _lastError;

    /// <inheritdoc />
    public ClusterSnapshot? Current => _current;

    /// <inheritdoc />
    public DateTimeOffset? LastRefresh => _current?.CapturedAt;

    /// <inheritdoc />
    public string? LastError => _lastError;

    /// <inheritdoc />
    public bool IsReady => _current is not null;

    /// <inheritdoc />
    public void SetSnapshot(ClusterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _current = snapshot;
        _lastError = null;
    }

    /// <inheritdoc />
    public void SetError(string error) => _lastError = error;
}
```

- [x] **Step 3: Create `src/Kafdoc.Application/Snapshot/RefreshOptions.cs`**

```csharp
namespace Kafdoc.Application.Snapshot;

/// <summary>Options controlling how often the cluster snapshot is refreshed.</summary>
public sealed class RefreshOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Kafka";

    /// <summary>How often to refresh the snapshot from the cluster. Defaults to one hour.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(1);
}
```

- [x] **Step 4: Create `src/Kafdoc.Application/Snapshot/IClusterRefreshService.cs`**

```csharp
using FluentResults;

namespace Kafdoc.Application.Snapshot;

/// <summary>Refreshes the in-memory cluster snapshot from Kafka.</summary>
public interface IClusterRefreshService
{
    /// <summary>
    /// Reads the cluster, builds the graph, and swaps it into the snapshot store.
    /// On failure the previous snapshot is left intact and the error is recorded.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the refresh.</param>
    /// <returns>A success result, or a failure describing what went wrong.</returns>
    Task<Result> RefreshAsync(CancellationToken cancellationToken);
}
```

- [x] **Step 5: Write the failing test `test/Kafdoc.ApplicationTest/Snapshot/ClusterRefreshServiceTests.cs`**

```csharp
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using Kafdoc.Application.Snapshot;
using Kafdoc.Domain.Graph;
using Kafdoc.Domain.Kafka;

namespace Kafdoc.ApplicationTest.Snapshot;

public class ClusterRefreshServiceTests
{
    private static RawClusterData EmptyRaw() => new([], [], [], []);

    [Fact]
    public async Task RefreshAsync_stores_snapshot_stamped_with_current_time()
    {
        // Arrange
        var reader = Substitute.For<IKafkaClusterReader>();
        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(EmptyRaw());
        var store = new SnapshotStore();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 12, 8, 0, 0, TimeSpan.Zero));
        var service = new ClusterRefreshService(reader, new ClusterGraphBuilder(), store, time);

        // Act
        var result = await service.RefreshAsync(CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(store.Current);
        Assert.Equal(time.GetUtcNow(), store.Current!.CapturedAt);
    }

    [Fact]
    public async Task RefreshAsync_keeps_previous_snapshot_when_read_fails()
    {
        // Arrange
        var reader = Substitute.For<IKafkaClusterReader>();
        var store = new SnapshotStore();
        var goodSnapshot = new ClusterSnapshot(
            new ClusterGraph([], [], [], [], [], [], []),
            new DateTimeOffset(2026, 6, 12, 7, 0, 0, TimeSpan.Zero));
        store.SetSnapshot(goodSnapshot);
        reader.ReadAsync(Arg.Any<CancellationToken>())
            .Returns<RawClusterData>(_ => throw new InvalidOperationException("broker down"));
        var service = new ClusterRefreshService(
            reader, new ClusterGraphBuilder(), store, new FakeTimeProvider());

        // Act
        var result = await service.RefreshAsync(CancellationToken.None);

        // Assert
        Assert.True(result.IsFailed);
        Assert.Same(goodSnapshot, store.Current);
        Assert.NotNull(store.LastError);
    }
}
```

> Add `<PackageVersion Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.0.9" />` to `Directory.Packages.props` and `<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />` to `test/Kafdoc.ApplicationTest/Kafdoc.ApplicationTest.csproj` (verify version via mslearn; on .NET 10 `FakeTimeProvider` lives in this package).

- [x] **Step 6: Run the test to verify it fails**

Run: `dotnet test --no-restore test/Kafdoc.ApplicationTest --filter-class "*ClusterRefreshServiceTests*"`
Expected: FAIL — `ClusterRefreshService` does not exist.

- [x] **Step 7: Implement `src/Kafdoc.Application/Snapshot/ClusterRefreshService.cs`**

```csharp
using FluentResults;

using Kafdoc.Domain.Graph;
using Kafdoc.Domain.Kafka;

namespace Kafdoc.Application.Snapshot;

/// <summary>Default <see cref="IClusterRefreshService"/>: reader → builder → store swap.</summary>
internal sealed class ClusterRefreshService(
    IKafkaClusterReader reader,
    ClusterGraphBuilder builder,
    ISnapshotStore store,
    TimeProvider timeProvider) : IClusterRefreshService
{
    /// <inheritdoc />
    public async Task<Result> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var raw = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var graph = builder.Build(raw);
            store.SetSnapshot(new ClusterSnapshot(graph, timeProvider.GetUtcNow()));
            return Result.Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Refresh must never crash the host; surface every failure as a result.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            store.SetError(ex.Message);
            return Result.Fail(new ExceptionalError(ex));
        }
    }
}
```

- [x] **Step 8: Run the test to verify it passes**

Run: `dotnet test --no-restore test/Kafdoc.ApplicationTest --filter-class "*ClusterRefreshServiceTests*"`
Expected: PASS.

- [x] **Step 9: Create the DTOs**

`src/Kafdoc.Application/Dtos/TopicSummaryDto.cs`:
```csharp
namespace Kafdoc.Application.Dtos;

/// <summary>Summary of a topic for the topics list.</summary>
/// <param name="Name">The topic name.</param>
/// <param name="PartitionCount">The number of partitions.</param>
/// <param name="ProducerCount">The number of distinct producing principals.</param>
/// <param name="ConsumerGroupCount">The number of consumer groups consuming the topic.</param>
public sealed record TopicSummaryDto(string Name, int PartitionCount, int ProducerCount, int ConsumerGroupCount);
```

`src/Kafdoc.Application/Dtos/TopicConsumerDto.cs`:
```csharp
namespace Kafdoc.Application.Dtos;

/// <summary>A consumer group consuming a topic, with the principals that back it.</summary>
/// <param name="GroupId">The consumer group id.</param>
/// <param name="State">The group state.</param>
/// <param name="Principals">Principals tied to the group via group-resource READ ACLs.</param>
public sealed record TopicConsumerDto(string GroupId, string State, IReadOnlyList<string> Principals);
```

`src/Kafdoc.Application/Dtos/TopicDetailDto.cs`:
```csharp
namespace Kafdoc.Application.Dtos;

/// <summary>Full detail of a single topic: producers and consumers.</summary>
/// <param name="Name">The topic name.</param>
/// <param name="PartitionCount">The number of partitions.</param>
/// <param name="Producers">Principals permitted to produce (WRITE ACL).</param>
/// <param name="ConsumerGroups">Consumer groups actually consuming the topic.</param>
/// <param name="ReadOnlyPrincipals">Principals permitted to read the topic but not tied to a consuming group.</param>
public sealed record TopicDetailDto(
    string Name,
    int PartitionCount,
    IReadOnlyList<string> Producers,
    IReadOnlyList<TopicConsumerDto> ConsumerGroups,
    IReadOnlyList<string> ReadOnlyPrincipals);
```

`src/Kafdoc.Application/Dtos/UserSummaryDto.cs`:
```csharp
namespace Kafdoc.Application.Dtos;

/// <summary>Summary of a user (principal) for the users list.</summary>
/// <param name="Principal">The principal.</param>
/// <param name="HasScramCredentials">Whether the principal has SCRAM credentials.</param>
/// <param name="ProducesCount">Number of topics the principal may produce to.</param>
/// <param name="ConsumesCount">Number of topics the principal may consume.</param>
public sealed record UserSummaryDto(string Principal, bool HasScramCredentials, int ProducesCount, int ConsumesCount);
```

`src/Kafdoc.Application/Dtos/UserDetailDto.cs`:
```csharp
namespace Kafdoc.Application.Dtos;

/// <summary>Full detail of a single user (principal).</summary>
/// <param name="Principal">The principal.</param>
/// <param name="HasScramCredentials">Whether the principal has SCRAM credentials.</param>
/// <param name="ProducesTopics">Topics the principal may produce to.</param>
/// <param name="ConsumesTopics">Topics the principal may consume.</param>
/// <param name="Groups">Consumer groups the principal backs.</param>
public sealed record UserDetailDto(
    string Principal,
    bool HasScramCredentials,
    IReadOnlyList<string> ProducesTopics,
    IReadOnlyList<string> ConsumesTopics,
    IReadOnlyList<string> Groups);
```

`src/Kafdoc.Application/Dtos/SnapshotStatusDto.cs`:
```csharp
namespace Kafdoc.Application.Dtos;

/// <summary>The status of the in-memory snapshot for display in the UI.</summary>
/// <param name="IsReady">Whether a snapshot has been captured.</param>
/// <param name="LastRefresh">When the last successful refresh completed.</param>
/// <param name="LastError">The last refresh error, if any.</param>
public sealed record SnapshotStatusDto(bool IsReady, DateTimeOffset? LastRefresh, string? LastError);
```

- [x] **Step 10: Create the query-service interfaces**

`src/Kafdoc.Application/Services/ITopicQueryService.cs`:
```csharp
using Kafdoc.Application.Dtos;

namespace Kafdoc.Application.Services;

/// <summary>Read-only queries over the current snapshot's topics.</summary>
public interface ITopicQueryService
{
    /// <summary>Returns all topics as summaries, ordered by name.</summary>
    IReadOnlyList<TopicSummaryDto> GetTopics();

    /// <summary>Returns full detail for one topic, or <see langword="null"/> if it is not present.</summary>
    /// <param name="name">The topic name.</param>
    TopicDetailDto? GetTopic(string name);
}
```

`src/Kafdoc.Application/Services/IUserQueryService.cs`:
```csharp
using Kafdoc.Application.Dtos;

namespace Kafdoc.Application.Services;

/// <summary>Read-only queries over the current snapshot's users.</summary>
public interface IUserQueryService
{
    /// <summary>Returns all users as summaries, ordered by principal.</summary>
    IReadOnlyList<UserSummaryDto> GetUsers();

    /// <summary>Returns full detail for one user, or <see langword="null"/> if not present.</summary>
    /// <param name="principal">The principal.</param>
    UserDetailDto? GetUser(string principal);
}
```

`src/Kafdoc.Application/Services/ISnapshotStatusService.cs`:
```csharp
using Kafdoc.Application.Dtos;

namespace Kafdoc.Application.Services;

/// <summary>Exposes snapshot status to the UI.</summary>
public interface ISnapshotStatusService
{
    /// <summary>Returns the current snapshot status.</summary>
    SnapshotStatusDto GetStatus();
}
```

- [x] **Step 11: Write the failing test `test/Kafdoc.ApplicationTest/Services/TopicQueryServiceTests.cs`**

```csharp
using Kafdoc.Application.Services;
using Kafdoc.Application.Snapshot;
using Kafdoc.Domain.Graph;

namespace Kafdoc.ApplicationTest.Services;

public class TopicQueryServiceTests
{
    private static SnapshotStore StoreWith(ClusterGraph graph)
    {
        var store = new SnapshotStore();
        store.SetSnapshot(new ClusterSnapshot(graph, DateTimeOffset.UnixEpoch));
        return store;
    }

    [Fact]
    public void GetTopics_returns_empty_when_no_snapshot()
    {
        // Arrange
        var service = new TopicQueryService(new SnapshotStore());

        // Act
        var topics = service.GetTopics();

        // Assert
        Assert.Empty(topics);
    }

    [Fact]
    public void GetTopics_counts_producers_and_consumer_groups()
    {
        // Arrange
        var graph = new ClusterGraph(
            Topics: [new KafkaTopic("orders", 3)],
            Users: [new KafkaUser("User:p", false), new KafkaUser("User:q", false)],
            ConsumerGroups: [new KafkaConsumerGroup("g1", "Stable", 1)],
            Producers: [new ProducerEdge("User:p", "orders"), new ProducerEdge("User:q", "orders")],
            Consumers: [],
            UserGroups: [],
            GroupConsumption: [new GroupTopicEdge("g1", "orders")]);
        var service = new TopicQueryService(StoreWith(graph));

        // Act
        var topic = Assert.Single(service.GetTopics());

        // Assert
        Assert.Equal("orders", topic.Name);
        Assert.Equal(2, topic.ProducerCount);
        Assert.Equal(1, topic.ConsumerGroupCount);
    }

    [Fact]
    public void GetTopic_returns_null_for_unknown_topic()
    {
        // Arrange
        var service = new TopicQueryService(StoreWith(
            new ClusterGraph([], [], [], [], [], [], [])));

        // Act + Assert
        Assert.Null(service.GetTopic("missing"));
    }

    [Fact]
    public void GetTopic_includes_producers_groups_and_read_only_principals()
    {
        // Arrange — User:r can read the topic but backs no consuming group
        var graph = new ClusterGraph(
            Topics: [new KafkaTopic("orders", 1)],
            Users: [new KafkaUser("User:p", false), new KafkaUser("User:c", false), new KafkaUser("User:r", false)],
            ConsumerGroups: [new KafkaConsumerGroup("g1", "Stable", 1)],
            Producers: [new ProducerEdge("User:p", "orders")],
            Consumers: [new ConsumerEdge("User:c", "orders"), new ConsumerEdge("User:r", "orders")],
            UserGroups: [new UserGroupEdge("User:c", "g1")],
            GroupConsumption: [new GroupTopicEdge("g1", "orders")]);
        var service = new TopicQueryService(StoreWith(graph));

        // Act
        var detail = service.GetTopic("orders");

        // Assert
        Assert.NotNull(detail);
        Assert.Equal(["User:p"], detail!.Producers);
        var group = Assert.Single(detail.ConsumerGroups);
        Assert.Equal("g1", group.GroupId);
        Assert.Equal(["User:c"], group.Principals);
        Assert.Equal(["User:r"], detail.ReadOnlyPrincipals);
    }
}
```

- [x] **Step 12: Run the test to verify it fails**

Run: `dotnet test --no-restore test/Kafdoc.ApplicationTest --filter-class "*TopicQueryServiceTests*"`
Expected: FAIL — `TopicQueryService` does not exist.

- [x] **Step 13: Implement `src/Kafdoc.Application/Services/TopicQueryService.cs`**

```csharp
using Kafdoc.Application.Dtos;
using Kafdoc.Application.Snapshot;
using Kafdoc.Domain.Graph;

namespace Kafdoc.Application.Services;

/// <summary>Computes topic views from the current snapshot.</summary>
internal sealed class TopicQueryService(ISnapshotStore store) : ITopicQueryService
{
    /// <inheritdoc />
    public IReadOnlyList<TopicSummaryDto> GetTopics()
    {
        var graph = store.Current?.Graph;
        if (graph is null)
        {
            return [];
        }

        var producersByTopic = graph.Producers
            .GroupBy(p => p.Topic, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Principal).Distinct(StringComparer.Ordinal).Count(), StringComparer.Ordinal);

        var groupsByTopic = graph.GroupConsumption
            .GroupBy(e => e.Topic, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.GroupId).Distinct(StringComparer.Ordinal).Count(), StringComparer.Ordinal);

        return graph.Topics
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new TopicSummaryDto(
                t.Name,
                t.PartitionCount,
                producersByTopic.GetValueOrDefault(t.Name),
                groupsByTopic.GetValueOrDefault(t.Name)))
            .ToList();
    }

    /// <inheritdoc />
    public TopicDetailDto? GetTopic(string name)
    {
        var graph = store.Current?.Graph;
        var topic = graph?.Topics.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
        if (graph is null || topic is null)
        {
            return null;
        }

        var producers = graph.Producers
            .Where(p => string.Equals(p.Topic, name, StringComparison.Ordinal))
            .Select(p => p.Principal)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var consumingGroupIds = graph.GroupConsumption
            .Where(e => string.Equals(e.Topic, name, StringComparison.Ordinal))
            .Select(e => e.GroupId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var principalsByGroup = graph.UserGroups
            .GroupBy(e => e.GroupId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Principal).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

        var groupStates = graph.ConsumerGroups.ToDictionary(g => g.GroupId, g => g.State, StringComparer.Ordinal);

        var consumerGroups = consumingGroupIds
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => new TopicConsumerDto(
                id,
                groupStates.GetValueOrDefault(id, "Unknown"),
                principalsByGroup.GetValueOrDefault(id, [])))
            .ToList();

        var principalsInGroups = principalsByGroup
            .Where(kvp => consumingGroupIds.Contains(kvp.Key, StringComparer.Ordinal))
            .SelectMany(kvp => kvp.Value)
            .ToHashSet(StringComparer.Ordinal);

        var readOnlyPrincipals = graph.Consumers
            .Where(c => string.Equals(c.Topic, name, StringComparison.Ordinal))
            .Select(c => c.Principal)
            .Where(p => !principalsInGroups.Contains(p))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        return new TopicDetailDto(topic.Name, topic.PartitionCount, producers, consumerGroups, readOnlyPrincipals);
    }
}
```

- [x] **Step 14: Run the test to verify it passes**

Run: `dotnet test --no-restore test/Kafdoc.ApplicationTest --filter-class "*TopicQueryServiceTests*"`
Expected: PASS.

- [x] **Step 15: Implement `src/Kafdoc.Application/Services/UserQueryService.cs`**

```csharp
using Kafdoc.Application.Dtos;
using Kafdoc.Application.Snapshot;
using Kafdoc.Domain.Graph;

namespace Kafdoc.Application.Services;

/// <summary>Computes user (principal) views from the current snapshot.</summary>
internal sealed class UserQueryService(ISnapshotStore store) : IUserQueryService
{
    /// <inheritdoc />
    public IReadOnlyList<UserSummaryDto> GetUsers()
    {
        var graph = store.Current?.Graph;
        if (graph is null)
        {
            return [];
        }

        var produces = Counts(graph.Producers.Select(p => (p.Principal, p.Topic)));
        var consumes = Counts(graph.Consumers.Select(c => (c.Principal, c.Topic)));

        return graph.Users
            .OrderBy(u => u.Principal, StringComparer.Ordinal)
            .Select(u => new UserSummaryDto(
                u.Principal,
                u.HasScramCredentials,
                produces.GetValueOrDefault(u.Principal),
                consumes.GetValueOrDefault(u.Principal)))
            .ToList();
    }

    /// <inheritdoc />
    public UserDetailDto? GetUser(string principal)
    {
        var graph = store.Current?.Graph;
        var user = graph?.Users.FirstOrDefault(u => string.Equals(u.Principal, principal, StringComparison.Ordinal));
        if (graph is null || user is null)
        {
            return null;
        }

        return new UserDetailDto(
            user.Principal,
            user.HasScramCredentials,
            Topics(graph.Producers.Where(p => Eq(p.Principal, principal)).Select(p => p.Topic)),
            Topics(graph.Consumers.Where(c => Eq(c.Principal, principal)).Select(c => c.Topic)),
            Topics(graph.UserGroups.Where(g => Eq(g.Principal, principal)).Select(g => g.GroupId)));
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

    private static IReadOnlyList<string> Topics(IEnumerable<string> source) =>
        source.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

    private static Dictionary<string, int> Counts(IEnumerable<(string Principal, string Topic)> edges) =>
        edges.GroupBy(e => e.Principal, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Topic).Distinct(StringComparer.Ordinal).Count(), StringComparer.Ordinal);
}
```

- [x] **Step 16: Create the status mapper and service**

`src/Kafdoc.Application/Mapper/SnapshotStatusMapper.cs`:
```csharp
using Kafdoc.Application.Dtos;
using Kafdoc.Application.Snapshot;

using Riok.Mapperly.Abstractions;

namespace Kafdoc.Application.Mapper;

/// <summary>Maps the snapshot store status to its DTO.</summary>
[Mapper]
internal static partial class SnapshotStatusMapper
{
    /// <summary>Maps an <see cref="ISnapshotStore"/> to a <see cref="SnapshotStatusDto"/>.</summary>
    /// <param name="store">The snapshot store.</param>
    internal static partial SnapshotStatusDto ToStatusDto(ISnapshotStore store);
}
```

`src/Kafdoc.Application/Services/SnapshotStatusService.cs`:
```csharp
using Kafdoc.Application.Dtos;
using Kafdoc.Application.Mapper;
using Kafdoc.Application.Snapshot;

namespace Kafdoc.Application.Services;

/// <summary>Exposes the snapshot store status as a DTO.</summary>
internal sealed class SnapshotStatusService(ISnapshotStore store) : ISnapshotStatusService
{
    /// <inheritdoc />
    public SnapshotStatusDto GetStatus() => SnapshotStatusMapper.ToStatusDto(store);
}
```

> If Mapperly complains about mapping `ISnapshotStore` (an interface with read-only properties), it maps property-by-property by name (`IsReady`, `LastRefresh`, `LastError`) which match the DTO exactly. If the generator rejects the interface source, replace the mapper body with a hand-written `new SnapshotStatusDto(store.IsReady, store.LastRefresh, store.LastError)` in `SnapshotStatusService` and delete the mapper file.

- [x] **Step 17: Build the Application project**

Run: `dotnet build --no-restore src/Kafdoc.Application/Kafdoc.Application.csproj -warnaserror`
Expected: PASS.

- [x] **Step 18: Commit**

```bash
git add src/Kafdoc.Application test/Kafdoc.ApplicationTest Directory.Packages.props
git commit -m "feat(application): snapshot store, refresh service, DTOs, query services"
```

---

## Task 6: Application — refresh hosted service and DI registration

**Files:**
- Create: `src/Kafdoc.Application/Snapshot/ClusterRefreshHostedService.cs`
- Modify: `src/Kafdoc.Application/Configuration.cs`

- [x] **Step 1: Create `src/Kafdoc.Application/Snapshot/ClusterRefreshHostedService.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kafdoc.Application.Snapshot;

/// <summary>
/// Refreshes the cluster snapshot once at startup, then on a fixed interval.
/// A failed refresh is logged and leaves the previous snapshot serving.
/// </summary>
internal sealed class ClusterRefreshHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<RefreshOptions> options,
    ILogger<ClusterRefreshHostedService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshOnceAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(options.Value.RefreshInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RefreshOnceAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        // The refresh service depends on the singleton store but is itself resolved
        // per cycle so the reader/admin client lifetime stays well-defined.
        await using var scope = scopeFactory.CreateAsyncScope();
        var refresh = scope.ServiceProvider.GetRequiredService<IClusterRefreshService>();
        var result = await refresh.RefreshAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsFailed)
        {
            logger.LogError("Cluster refresh failed: {Errors}", string.Join("; ", result.Errors.Select(e => e.Message)));
        }
        else
        {
            logger.LogInformation("Cluster snapshot refreshed.");
        }
    }
}
```

- [x] **Step 2: Add `Microsoft.Extensions.Logging.Abstractions` if needed**

`ILogger<T>` comes transitively via Hosting.Abstractions; if the build reports it missing, add `<PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.9" />` to `Directory.Packages.props` and a version-less `PackageReference` to the Application csproj.

- [x] **Step 3: Register everything in `src/Kafdoc.Application/Configuration.cs`**

Replace the body of `ConfigureApplication`:

```csharp
        services.AddOptions<Snapshot.RefreshOptions>()
            .Bind(configuration.GetSection(Snapshot.RefreshOptions.SectionName));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<Snapshot.ISnapshotStore, Snapshot.SnapshotStore>();
        services.AddScoped<Snapshot.IClusterRefreshService, Snapshot.ClusterRefreshService>();

        services.AddSingleton<Services.ITopicQueryService, Services.TopicQueryService>();
        services.AddSingleton<Services.IUserQueryService, Services.UserQueryService>();
        services.AddSingleton<Services.ISnapshotStatusService, Services.SnapshotStatusService>();

        services.AddHostedService<Snapshot.ClusterRefreshHostedService>();
```

Add the required usings at the top of the file:
```csharp
using System;
```

- [x] **Step 4: Build and run all Application tests**

Run: `dotnet build --no-restore src/Kafdoc.Application/Kafdoc.Application.csproj -warnaserror`
Then: `dotnet test --no-restore test/Kafdoc.ApplicationTest`
Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add src/Kafdoc.Application
git commit -m "feat(application): background refresh hosted service and DI wiring"
```

---

## Task 7: Infrastructure — Confluent Kafka adapter

**Files:**
- Create: `src/Kafdoc.Infrastructure/Kafka/KafkaConnectionOptions.cs`, `src/Kafdoc.Infrastructure/Kafka/ConfluentKafkaClusterReader.cs`
- Modify: `src/Kafdoc.Infrastructure/Configuration.cs`

> This task talks to the real Confluent.Kafka API; method/enum names below are from `Confluent.Kafka` v2.x. Confirm exact member names with the `mslearn` MCP server or the package's IntelliSense as you write. The adapter performs **fetching and mapping only** — no derivation.

- [x] **Step 1: Create `src/Kafdoc.Infrastructure/Kafka/KafkaConnectionOptions.cs`**

```csharp
namespace Kafdoc.Infrastructure.Kafka;

/// <summary>Connection settings for the Kafka Admin client.</summary>
public sealed class KafkaConnectionOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Kafka";

    /// <summary>Comma-separated bootstrap servers, e.g. <c>broker:9092</c>.</summary>
    public string BootstrapServers { get; set; } = string.Empty;

    /// <summary>Security protocol, e.g. <c>SaslSsl</c> or <c>SaslPlaintext</c>.</summary>
    public string SecurityProtocol { get; set; } = "SaslSsl";

    /// <summary>SASL mechanism, e.g. <c>ScramSha512</c>.</summary>
    public string SaslMechanism { get; set; } = "ScramSha512";

    /// <summary>SASL username.</summary>
    public string SaslUsername { get; set; } = string.Empty;

    /// <summary>SASL password.</summary>
    public string SaslPassword { get; set; } = string.Empty;

    /// <summary>Optional path to a CA certificate bundle for TLS.</summary>
    public string? SslCaLocation { get; set; }

    /// <summary>Admin request timeout. Defaults to 30 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
```

- [x] **Step 2: Create `src/Kafdoc.Infrastructure/Kafka/ConfluentKafkaClusterReader.cs`**

```csharp
using Confluent.Kafka;
using Confluent.Kafka.Admin;

using Microsoft.Extensions.Options;

using Kafdoc.Domain.Kafka;

namespace Kafdoc.Infrastructure.Kafka;

/// <summary>
/// Reads raw facts from a Kafka cluster using the Confluent.Kafka Admin API.
/// Fetches and maps only; all graph derivation lives in the Domain layer.
/// </summary>
internal sealed class ConfluentKafkaClusterReader(
    IAdminClient adminClient,
    IOptions<KafkaConnectionOptions> options) : IKafkaClusterReader
{
    private readonly TimeSpan _timeout = options.Value.RequestTimeout;

    /// <inheritdoc />
    public async Task<RawClusterData> ReadAsync(CancellationToken cancellationToken)
    {
        var topics = ReadTopics();
        var acls = await ReadAclsAsync().ConfigureAwait(false);
        var groups = await ReadConsumerGroupsAsync().ConfigureAwait(false);
        var scramUsers = await ReadScramUsersAsync().ConfigureAwait(false);

        return new RawClusterData(topics, acls, groups, scramUsers);
    }

    private IReadOnlyList<RawTopic> ReadTopics()
    {
        var metadata = adminClient.GetMetadata(_timeout);
        return metadata.Topics
            .Where(t => !t.Topic.StartsWith("__", StringComparison.Ordinal)) // skip internal topics
            .Select(t => new RawTopic(t.Topic, t.Partitions.Count))
            .ToList();
    }

    private async Task<IReadOnlyList<RawAcl>> ReadAclsAsync()
    {
        var filter = new AclBindingFilter
        {
            PatternFilter = new ResourcePatternFilter
            {
                Type = ResourceType.Any,
                ResourcePatternType = ResourcePatternType.Any,
            },
            EntryFilter = new AccessControlEntryFilter
            {
                Operation = AclOperation.Any,
                PermissionType = AclPermissionType.Any,
            },
        };

        var result = await adminClient.DescribeAclsAsync(filter).ConfigureAwait(false);
        return result.AclBindings.Select(MapAcl).ToList();
    }

    private async Task<IReadOnlyList<RawConsumerGroup>> ReadConsumerGroupsAsync()
    {
        var listing = await adminClient.ListConsumerGroupsAsync().ConfigureAwait(false);
        var groupIds = listing.Valid.Select(g => g.GroupId).ToList();
        if (groupIds.Count == 0)
        {
            return [];
        }

        var described = await adminClient
            .DescribeConsumerGroupsAsync(groupIds)
            .ConfigureAwait(false);

        var offsets = await adminClient
            .ListConsumerGroupOffsetsAsync(
                groupIds.Select(id => new ConsumerGroupTopicPartitions(id, partitions: null)).ToList())
            .ConfigureAwait(false);

        var topicsByGroup = offsets.ToDictionary(
            o => o.Group,
            o => o.Partitions
                .Where(p => p.Offset != Offset.Unset)
                .Select(p => p.TopicPartition.Topic)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            StringComparer.Ordinal);

        return described.ConsumerGroupDescriptions
            .Select(d => new RawConsumerGroup(
                d.GroupId,
                d.State.ToString(),
                d.Members.Count,
                topicsByGroup.GetValueOrDefault(d.GroupId, [])))
            .ToList();
    }

    private async Task<IReadOnlyList<RawScramUser>> ReadScramUsersAsync()
    {
        // Passing an empty list describes all users with SCRAM credentials.
        var result = await adminClient
            .DescribeUserScramCredentialsAsync([])
            .ConfigureAwait(false);

        return result.UserScramCredentialsDescriptions
            .Select(d => new RawScramUser($"User:{d.User}"))
            .ToList();
    }

    private static RawAcl MapAcl(AclBinding binding) => new(
        binding.Entry.Principal,
        MapResourceType(binding.Pattern.Type),
        binding.Pattern.Name,
        MapPatternType(binding.Pattern.ResourcePatternType),
        MapOperation(binding.Entry.Operation),
        MapPermission(binding.Entry.PermissionType));

    private static KafkaResourceType MapResourceType(ResourceType type) => type switch
    {
        ResourceType.Topic => KafkaResourceType.Topic,
        ResourceType.Group => KafkaResourceType.Group,
        ResourceType.Broker => KafkaResourceType.Cluster,
        ResourceType.TransactionalId => KafkaResourceType.TransactionalId,
        _ => KafkaResourceType.Other,
    };

    private static KafkaResourcePatternType MapPatternType(ResourcePatternType type) => type switch
    {
        ResourcePatternType.Literal => KafkaResourcePatternType.Literal,
        ResourcePatternType.Prefixed => KafkaResourcePatternType.Prefixed,
        _ => KafkaResourcePatternType.Other,
    };

    private static KafkaAclOperation MapOperation(AclOperation op) => op switch
    {
        AclOperation.Read => KafkaAclOperation.Read,
        AclOperation.Write => KafkaAclOperation.Write,
        AclOperation.All => KafkaAclOperation.All,
        _ => KafkaAclOperation.Other,
    };

    private static KafkaAclPermission MapPermission(AclPermissionType permission) => permission switch
    {
        AclPermissionType.Allow => KafkaAclPermission.Allow,
        AclPermissionType.Deny => KafkaAclPermission.Deny,
        _ => KafkaAclPermission.Other,
    };
}
```

> If a member name differs in your Confluent.Kafka version (e.g. the offsets result shape, or `ResourceType.Broker` vs `Cluster`), adjust the mapping and keep the `Raw*` output identical. The Domain tests do not depend on Confluent types, so the contract is stable.

- [x] **Step 3: Replace `src/Kafdoc.Infrastructure/Configuration.cs`**

```csharp
using Confluent.Kafka;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Kafdoc.Domain.Kafka;
using Kafdoc.Infrastructure.Kafka;

namespace Kafdoc.Infrastructure;

/// <summary>Dependency injection registrations for the Infrastructure layer.</summary>
public static class Configuration
{
    /// <summary>
    /// Registers the Kafka admin client and cluster reader.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">The application configuration.</param>
    public static void ConfigureInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KafkaConnectionOptions>()
            .Bind(configuration.GetSection(KafkaConnectionOptions.SectionName));

        services.AddSingleton<IAdminClient>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<KafkaConnectionOptions>>().Value;
            var config = new AdminClientConfig
            {
                BootstrapServers = options.BootstrapServers,
                SecurityProtocol = Enum.Parse<SecurityProtocol>(options.SecurityProtocol, ignoreCase: true),
                SaslMechanism = Enum.Parse<SaslMechanism>(options.SaslMechanism, ignoreCase: true),
                SaslUsername = options.SaslUsername,
                SaslPassword = options.SaslPassword,
                SslCaLocation = options.SslCaLocation,
            };
            return new AdminClientBuilder(config).Build();
        });

        services.AddSingleton<IKafkaClusterReader, ConfluentKafkaClusterReader>();
    }
}
```

- [x] **Step 4: Build the Infrastructure project**

Run: `dotnet build --no-restore src/Kafdoc.Infrastructure/Kafdoc.Infrastructure.csproj -warnaserror`
Expected: PASS. Fix any Confluent member-name mismatches now.

- [x] **Step 5: Commit**

```bash
git add src/Kafdoc.Infrastructure
git commit -m "feat(infrastructure): Confluent Kafka cluster reader and DI"
```

---

## Task 8: Web — topics, topic detail, users pages, and refresh status

**Files:**
- Create: `src/Kafdoc.Web/Components/Pages/Topics.razor`, `TopicDetail.razor`, `Users.razor`, `src/Kafdoc.Web/Components/Layout/RefreshStatus.razor`
- Modify: `src/Kafdoc.Web/Components/Layout/NavMenu.razor`, `src/Kafdoc.Web/Components/Pages/NotFound.razor` (leave as-is), `src/Kafdoc.Web/appsettings.json`
- Test: `test/Kafdoc.WebTest/TopicsPageTests.cs`

- [x] **Step 1: Create `src/Kafdoc.Web/Components/Pages/Topics.razor`**

```razor
@page "/"
@rendermode InteractiveServer
@using Kafdoc.Application.Services
@inject ITopicQueryService TopicQuery
@inject ISnapshotStatusService Status

<PageTitle>Topics</PageTitle>

<h1>Topics</h1>

<RefreshStatus />

@if (!Status.GetStatus().IsReady)
{
    <p><em>Loading cluster data…</em></p>
}
else
{
    <input class="form-control mb-3" placeholder="Filter topics…" @bind="_filter" @bind:event="oninput" />

    <table class="table">
        <thead>
            <tr>
                <th>Topic</th>
                <th>Partitions</th>
                <th>Producers</th>
                <th>Consumer groups</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var topic in Filtered())
            {
                <tr>
                    <td><a href="@($"/topics/{Uri.EscapeDataString(topic.Name)}")">@topic.Name</a></td>
                    <td>@topic.PartitionCount</td>
                    <td>@topic.ProducerCount</td>
                    <td>@topic.ConsumerGroupCount</td>
                </tr>
            }
        </tbody>
    </table>
}

@code {
    private string _filter = string.Empty;

    private IEnumerable<Kafdoc.Application.Dtos.TopicSummaryDto> Filtered() =>
        TopicQuery.GetTopics()
            .Where(t => string.IsNullOrWhiteSpace(_filter)
                || t.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase));
}
```

- [x] **Step 2: Create `src/Kafdoc.Web/Components/Pages/TopicDetail.razor`**

```razor
@page "/topics/{Name}"
@rendermode InteractiveServer
@using Kafdoc.Application.Services
@inject ITopicQueryService TopicQuery

<PageTitle>Topic: @Name</PageTitle>

<p><a href="/">&larr; All topics</a></p>

@{
    var detail = TopicQuery.GetTopic(Name);
}

@if (detail is null)
{
    <h1>Topic not found</h1>
    <p>No topic named <code>@Name</code> in the current snapshot.</p>
}
else
{
    <h1>@detail.Name</h1>
    <p>@detail.PartitionCount partition(s)</p>

    <h2>Producers</h2>
    @if (detail.Producers.Count == 0)
    {
        <p><em>No producers (no WRITE ACLs).</em></p>
    }
    else
    {
        <ul>@foreach (var p in detail.Producers) { <li>@p</li> }</ul>
    }

    <h2>Consumer groups</h2>
    @if (detail.ConsumerGroups.Count == 0)
    {
        <p><em>No consumer groups consuming this topic.</em></p>
    }
    else
    {
        <table class="table">
            <thead><tr><th>Group</th><th>State</th><th>Principals</th></tr></thead>
            <tbody>
                @foreach (var g in detail.ConsumerGroups)
                {
                    <tr>
                        <td>@g.GroupId</td>
                        <td>@g.State</td>
                        <td>@(g.Principals.Count == 0 ? "—" : string.Join(", ", g.Principals))</td>
                    </tr>
                }
            </tbody>
        </table>
    }

    @if (detail.ReadOnlyPrincipals.Count > 0)
    {
        <h2>Read-only principals (permitted, no active group)</h2>
        <ul>@foreach (var p in detail.ReadOnlyPrincipals) { <li>@p</li> }</ul>
    }
}

@code {
    /// <summary>The topic name from the route.</summary>
    [Parameter]
    public string Name { get; set; } = string.Empty;
}
```

- [x] **Step 3: Create `src/Kafdoc.Web/Components/Pages/Users.razor`**

```razor
@page "/users"
@rendermode InteractiveServer
@using Kafdoc.Application.Services
@inject IUserQueryService UserQuery
@inject ISnapshotStatusService Status

<PageTitle>Users</PageTitle>

<h1>Users</h1>

<RefreshStatus />

@if (!Status.GetStatus().IsReady)
{
    <p><em>Loading cluster data…</em></p>
}
else
{
    <table class="table">
        <thead>
            <tr><th>Principal</th><th>SCRAM</th><th>Produces</th><th>Consumes</th></tr>
        </thead>
        <tbody>
            @foreach (var u in UserQuery.GetUsers())
            {
                <tr>
                    <td>@u.Principal</td>
                    <td>@(u.HasScramCredentials ? "yes" : "—")</td>
                    <td>@u.ProducesCount</td>
                    <td>@u.ConsumesCount</td>
                </tr>
            }
        </tbody>
    </table>
}
```

- [x] **Step 4: Create `src/Kafdoc.Web/Components/Layout/RefreshStatus.razor`**

```razor
@rendermode InteractiveServer
@using Kafdoc.Application.Services
@using Kafdoc.Application.Snapshot
@inject ISnapshotStatusService Status
@inject IServiceScopeFactory ScopeFactory

<div class="alert alert-light d-flex justify-content-between align-items-center" role="status">
    <span>
        @{
            var status = Status.GetStatus();
        }
        @if (status.LastRefresh is { } when)
        {
            <span>Last refreshed: @when.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")</span>
        }
        else
        {
            <span>Never refreshed</span>
        }
        @if (status.LastError is { } error)
        {
            <span class="text-danger ms-2">⚠ Last refresh failed: @error</span>
        }
    </span>
    <button class="btn btn-sm btn-outline-primary" @onclick="RefreshNow" disabled="@_refreshing">
        @(_refreshing ? "Refreshing…" : "Refresh now")
    </button>
</div>

@code {
    private bool _refreshing;

    private async Task RefreshNow()
    {
        _refreshing = true;
        try
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var refresh = scope.ServiceProvider.GetRequiredService<IClusterRefreshService>();
            await refresh.RefreshAsync(CancellationToken.None);
        }
        finally
        {
            _refreshing = false;
        }
    }
}
```

> `RefreshStatus` injects `IClusterRefreshService` via a fresh scope (it is scoped). This is the only place a Web component reaches an Application orchestration service directly, which is allowed (Web → Application). It does not touch Domain or Infrastructure types, so `WebIsolationTests` stays green.

- [x] **Step 5: Update `src/Kafdoc.Web/Components/Layout/NavMenu.razor`** nav links

Replace the three `<div class="nav-item px-3">` blocks (Home/Counter/Weather) with:

```razor
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="" Match="NavLinkMatch.All">
                <span class="bi bi-list-nested-nav-menu" aria-hidden="true"></span> Topics
            </NavLink>
        </div>

        <div class="nav-item px-3">
            <NavLink class="nav-link" href="users">
                <span class="bi bi-person-fill-nav-menu" aria-hidden="true"></span> Users
            </NavLink>
        </div>
```

- [x] **Step 6: Add a `Kafka` config section to `src/Kafdoc.Web/appsettings.json`**

Add this top-level section (secrets stay empty here; supply via env/user-secrets):

```json
  "Kafka": {
    "BootstrapServers": "",
    "SecurityProtocol": "SaslSsl",
    "SaslMechanism": "ScramSha512",
    "SaslUsername": "",
    "SaslPassword": "",
    "RequestTimeout": "00:00:30",
    "RefreshInterval": "01:00:00"
  }
```

- [x] **Step 7: Write the bUnit test `test/Kafdoc.WebTest/TopicsPageTests.cs`**

```csharp
using Bunit;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using Kafdoc.Application.Dtos;
using Kafdoc.Application.Services;
using Kafdoc.Web.Components.Pages;

namespace Kafdoc.WebTest;

public sealed class TopicsPageTests : Bunit.BunitContext
{
    [Fact]
    public void Topics_renders_loading_message_when_snapshot_not_ready()
    {
        // Arrange
        var topicQuery = Substitute.For<ITopicQueryService>();
        var status = Substitute.For<ISnapshotStatusService>();
        status.GetStatus().Returns(new SnapshotStatusDto(IsReady: false, LastRefresh: null, LastError: null));
        Services.AddSingleton(topicQuery);
        Services.AddSingleton(status);

        // Act
        var cut = Render<Topics>();

        // Assert
        Assert.Contains("Loading cluster data", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Topics_renders_a_row_per_topic_when_ready()
    {
        // Arrange
        var topicQuery = Substitute.For<ITopicQueryService>();
        topicQuery.GetTopics().Returns(
        [
            new TopicSummaryDto("orders", 3, 1, 2),
            new TopicSummaryDto("billing", 1, 0, 1),
        ]);
        var status = Substitute.For<ISnapshotStatusService>();
        status.GetStatus().Returns(new SnapshotStatusDto(IsReady: true, LastRefresh: DateTimeOffset.UnixEpoch, LastError: null));
        Services.AddSingleton(topicQuery);
        Services.AddSingleton(status);

        // Act
        var cut = Render<Topics>();

        // Assert
        Assert.Contains("orders", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("billing", cut.Markup, StringComparison.Ordinal);
    }
}
```

> `RefreshStatus` is rendered inside `Topics` and injects `IServiceScopeFactory`; bUnit provides one through its service provider, and `GetStatus()` is stubbed, so no extra setup is needed. If bUnit cannot resolve `IClusterRefreshService` during render, it is not needed at render time (only on button click), so the test stays green. If render throws resolving it, register `Services.AddScoped(_ => Substitute.For<IClusterRefreshService>());`.

- [x] **Step 8: Run the Web tests**

Run: `dotnet test --no-restore test/Kafdoc.WebTest`
Expected: PASS.

- [x] **Step 9: Build the whole solution**

Run: `dotnet build --no-restore -warnaserror`
Expected: PASS (including architecture tests now that all anchor types exist).

- [x] **Step 10: Run the architecture tests**

Run: `dotnet test --no-restore test/Kafdoc.ArchitectureTest`
Expected: PASS. If `WebIsolationTests` fails, confirm no page imports a Domain/Infrastructure namespace (only `Kafdoc.Application.*`).

- [x] **Step 11: Commit**

```bash
git add src/Kafdoc.Web test/Kafdoc.WebTest
git commit -m "feat(web): topics, topic detail, users pages with refresh status"
```

---

## Task 9: Infrastructure integration tests against a secured Kafka broker

Spin up a secured KRaft broker via Testcontainers (SASL_PLAINTEXT + `StandardAuthorizer`), seed topics/ACLs/a SCRAM user/a consumer group, and assert `ConfluentKafkaClusterReader` reads them back.

**Files:**
- Create: `test/Kafdoc.InfrastructureTest/Kafdoc.InfrastructureTest.csproj`, `test/Kafdoc.InfrastructureTest/SecuredKafkaContainer.cs`, `test/Kafdoc.InfrastructureTest/ConfluentKafkaClusterReaderTests.cs`
- Modify: `Kafdoc.slnx`

- [x] **Step 1: Create `test/Kafdoc.InfrastructureTest/Kafdoc.InfrastructureTest.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Confluent.Kafka" />
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.Testing.Extensions.CodeCoverage" />
    <PackageReference Include="Testcontainers" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.analyzers" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="../xunit.runner.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/Kafdoc.Infrastructure/Kafdoc.Infrastructure.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

- [x] **Step 2: Register the new test project in `Kafdoc.slnx`**

Add inside the `/test/` folder:
```xml
    <Project Path="test/Kafdoc.InfrastructureTest/Kafdoc.InfrastructureTest.csproj" />
```

- [x] **Step 3: Create `test/Kafdoc.InfrastructureTest/SecuredKafkaContainer.cs`**

A single-node KRaft broker configured for SASL_PLAINTEXT with the standard authorizer and a super-user, built with the generic `ContainerBuilder`. Uses the `apache/kafka` image.

```csharp
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Kafdoc.InfrastructureTest;

/// <summary>
/// A single-node KRaft Kafka broker secured with SASL_PLAINTEXT (SCRAM-SHA-512) and the
/// standard authorizer. The <c>admin</c> super-user is provisioned via broker JAAS so the
/// admin client can authenticate before any other SCRAM users are created.
/// </summary>
internal sealed class SecuredKafkaContainer
{
    private const int Port = 9092;
    private readonly IContainer _container;

    public SecuredKafkaContainer()
    {
        _container = new ContainerBuilder()
            .WithImage("apache/kafka:3.8.0")
            .WithPortBinding(Port, assignRandomHostPort: true)
            .WithEnvironment("KAFKA_NODE_ID", "1")
            .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
            .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", "1@localhost:9093")
            .WithEnvironment("KAFKA_LISTENERS", "SASL://0.0.0.0:9092,CONTROLLER://0.0.0.0:9093")
            .WithEnvironment("KAFKA_ADVERTISED_LISTENERS", "SASL://localhost:9092")
            .WithEnvironment("KAFKA_LISTENER_SECURITY_PROTOCOL_MAP", "SASL:SASL_PLAINTEXT,CONTROLLER:PLAINTEXT")
            .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
            .WithEnvironment("KAFKA_INTER_BROKER_LISTENER_NAME", "SASL")
            .WithEnvironment("KAFKA_SASL_ENABLED_MECHANISMS", "SCRAM-SHA-512")
            .WithEnvironment("KAFKA_SASL_MECHANISM_INTER_BROKER_PROTOCOL", "SCRAM-SHA-512")
            .WithEnvironment("KAFKA_AUTHORIZER_CLASS_NAME", "org.apache.kafka.metadata.authorizer.StandardAuthorizer")
            .WithEnvironment("KAFKA_SUPER_USERS", "User:admin")
            .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "1")
            // Provision the admin super-user's SCRAM credential at format time.
            .WithEnvironment("KAFKA_CONTROLLER_QUORUM_BOOTSTRAP_SERVERS", "localhost:9093")
            .WithEnvironment(
                "KAFKA_LISTENER_NAME_SASL_SCRAM-SHA-512_SASL_JAAS_CONFIG",
                "org.apache.kafka.common.security.scram.ScramLoginModule required;")
            .WithEnvironment(
                "KAFKA_OPTS",
                "-Dorg.apache.kafka.disallowed.login.modules=")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Kafka Server started"))
            .Build();
    }

    /// <summary>The host bootstrap servers once started.</summary>
    public string BootstrapServers => $"localhost:{_container.GetMappedPublicPort(Port)}";

    public Task StartAsync(CancellationToken ct) => _container.StartAsync(ct);

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Runs a kafka admin shell command inside the broker container.</summary>
    public Task<ExecResult> ExecAsync(IList<string> command, CancellationToken ct) =>
        _container.ExecAsync(command, ct);
}
```

> **This container config is the riskiest part of the plan.** Secured KRaft brokers are sensitive to exact env-var spelling and to provisioning the bootstrap SCRAM admin credential. Verify against the Apache Kafka Docker image docs (`apache/kafka` README) and the Testcontainers .NET docs via mslearn/WebFetch. If bootstrapping the SCRAM admin via env proves unreliable, fall back to: start the broker with a PLAINTEXT inter-broker listener + `KAFKA_SUPER_USERS=User:ANONYMOUS`, create the admin SCRAM credential with `kafka-storage`/`kafka-configs` via `ExecAsync` before connecting, then connect over SASL. Spike this container in isolation (a throwaway `[Fact]` that just starts it and runs `GetMetadata`) before writing the assertions in Step 4.

- [x] **Step 4: Create `test/Kafdoc.InfrastructureTest/ConfluentKafkaClusterReaderTests.cs`**

Seed the cluster with the admin client (create a topic, an ACL, a SCRAM user, and commit an offset for a group), then exercise the reader. Use the real `ConfluentKafkaClusterReader` with hand-built options.

```csharp
using Confluent.Kafka;
using Confluent.Kafka.Admin;

using Microsoft.Extensions.Options;

using Kafdoc.Domain.Kafka;
using Kafdoc.Infrastructure.Kafka;

namespace Kafdoc.InfrastructureTest;

public sealed class ConfluentKafkaClusterReaderTests : IAsyncLifetime
{
    private readonly SecuredKafkaContainer _broker = new();
    private IAdminClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        await _broker.StartAsync(TestContext.Current.CancellationToken);
        _admin = new AdminClientBuilder(AdminConfig()).Build();
        await SeedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _admin?.Dispose();
        await _broker.DisposeAsync();
    }

    private AdminClientConfig AdminConfig() => new()
    {
        BootstrapServers = _broker.BootstrapServers,
        SecurityProtocol = SecurityProtocol.SaslPlaintext,
        SaslMechanism = SaslMechanism.ScramSha512,
        SaslUsername = "admin",
        SaslPassword = "admin-secret",
    };

    private async Task SeedAsync()
    {
        await _admin.CreateTopicsAsync([new TopicSpecification { Name = "orders", NumPartitions = 1, ReplicationFactor = 1 }]);

        await _admin.CreateAclsAsync(
        [
            new AclBinding
            {
                Pattern = new ResourcePattern { Type = ResourceType.Topic, Name = "orders", ResourcePatternType = ResourcePatternType.Literal },
                Entry = new AccessControlEntry { Principal = "User:svc-orders", Host = "*", Operation = AclOperation.Write, PermissionType = AclPermissionType.Allow },
            },
        ]);

        await _admin.AlterUserScramCredentialsAsync(
        [
            new UserScramCredentialUpsertion(
                "svc-orders",
                new ScramCredentialInfo(ScramMechanism.ScramSha512, iterations: 4096),
                "svc-orders-secret"),
        ]);
    }

    [Fact]
    public async Task ReadAsync_returns_seeded_topic_acl_and_scram_user()
    {
        // Arrange
        var reader = new ConfluentKafkaClusterReader(
            _admin,
            Options.Create(new KafkaConnectionOptions { RequestTimeout = TimeSpan.FromSeconds(30) }));

        // Act
        var data = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(data.Topics, t => t.Name == "orders");
        Assert.Contains(data.Acls, a =>
            a.Principal == "User:svc-orders"
            && a.ResourceType == KafkaResourceType.Topic
            && a.ResourceName == "orders"
            && a.Operation == KafkaAclOperation.Write
            && a.Permission == KafkaAclPermission.Allow);
        Assert.Contains(data.ScramUsers, u => u.Principal == "User:svc-orders");
    }
}
```

> `TestContext.Current.CancellationToken` is the xUnit v3 idiom for the per-test token. Reuse the started broker across tests with an `IClassFixture`/`IAsyncLifetime` if you add more cases, to avoid paying broker startup per test.

- [x] **Step 5: Run the integration test**

Run: `dotnet test --no-restore test/Kafdoc.InfrastructureTest`
Expected: PASS. Requires a reachable Docker daemon (see Task 10 devcontainer prerequisite). If the broker won't start, iterate on `SecuredKafkaContainer` per the Step 3 fallback before touching assertions.

- [x] **Step 6: Commit**

```bash
git add test/Kafdoc.InfrastructureTest Kafdoc.slnx
git commit -m "test(infrastructure): secured-broker integration tests for the Kafka reader"
```

---

## Task 10: Devcontainer Kafka service and documentation

**Files:**
- Modify: `.devcontainer/docker-compose.yml`, `.devcontainer/devcontainer.json`, `CLAUDE.md`

- [x] **Step 1: Replace the Postgres `db` service with a secured Kafka service in `.devcontainer/docker-compose.yml`**

Replace the entire file with:

```yaml
services:
  app:
    build:
      context: .
      dockerfile: Dockerfile
    volumes:
      - ../..:/workspaces:cached
    command: sleep infinity
    network_mode: service:kafka

  kafka:
    image: apache/kafka:3.8.0
    restart: unless-stopped
    environment:
      KAFKA_NODE_ID: "1"
      KAFKA_PROCESS_ROLES: "broker,controller"
      KAFKA_CONTROLLER_QUORUM_VOTERS: "1@localhost:9093"
      KAFKA_LISTENERS: "SASL://0.0.0.0:9092,CONTROLLER://0.0.0.0:9093"
      KAFKA_ADVERTISED_LISTENERS: "SASL://localhost:9092"
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: "SASL:SASL_PLAINTEXT,CONTROLLER:PLAINTEXT"
      KAFKA_CONTROLLER_LISTENER_NAMES: "CONTROLLER"
      KAFKA_INTER_BROKER_LISTENER_NAME: "SASL"
      KAFKA_SASL_ENABLED_MECHANISMS: "SCRAM-SHA-512"
      KAFKA_SASL_MECHANISM_INTER_BROKER_PROTOCOL: "SCRAM-SHA-512"
      KAFKA_AUTHORIZER_CLASS_NAME: "org.apache.kafka.metadata.authorizer.StandardAuthorizer"
      KAFKA_SUPER_USERS: "User:admin"
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: "1"
      KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR: "1"
      KAFKA_TRANSACTION_STATE_LOG_MIN_ISR: "1"
      KAFKA_LISTENER_NAME_SASL_SCRAM-SHA-512_SASL_JAAS_CONFIG: "org.apache.kafka.common.security.scram.ScramLoginModule required;"
```

> Use the **same** broker configuration validated in Task 9's `SecuredKafkaContainer`. If Task 9 settled on the PLAINTEXT-inter-broker + `kafka-configs` admin-provisioning fallback, mirror that here (and add a small init step or documented manual `kafka-configs` command to create the `admin` SCRAM credential and seed sample topics/ACLs/users for a useful local graph).

- [x] **Step 2: Update `.devcontainer/devcontainer.json`**

Change the `postCreateCommand` (drop `dotnet-ef`):

```json
  "postCreateCommand": "dotnet restore"
```

(Leave the rest of the file unchanged; the `ms-ossdata.vscode-pgsql` extension can be removed from `extensions` since Postgres is gone.)

- [x] **Step 3: Document the Docker-daemon prerequisite for integration tests**

Add a short note to `.devcontainer/devcontainer.json` is not needed; instead document in `CLAUDE.md` (next step) that `test/Kafdoc.InfrastructureTest` needs a reachable Docker daemon (Testcontainers).

- [x] **Step 4: Rewrite the `## Overview`, `## Commands`, and remove the stale DB sections in `CLAUDE.md`**

Replace the `## Overview` body (`TODO...`) with:

```markdown
Kafdoc documents a single secured Kafka cluster. On startup and on a configurable
interval (default hourly) it reads topics, ACLs, users, and consumer groups via the
Kafka Admin API, builds an in-memory producer/consumer graph (producers from WRITE
ACLs, consumers from READ ACLs + consumer-group offsets, the user↔group bridge from
group-resource READ ACLs), and presents it in a Blazor Server UI (topics list, topic
detail, users). The data lives only in memory; there is no database.
```

In `## Commands`, replace the web-run line and add the integration-test note:

```markdown
# Run the web app (configure the Kafka section in appsettings / user-secrets first)
cd src/Kafdoc.Web && dotnet run

# Integration tests spin up a real secured Kafka broker via Testcontainers and need a
# reachable Docker daemon:
dotnet test --no-restore test/Kafdoc.InfrastructureTest
```

In `## Architecture`, replace the four bullet descriptions to match the new design:
- **Kafdoc.Domain** — immutable read model (`KafkaTopic`/`KafkaUser`/`KafkaConsumerGroup`, graph edges, `ClusterSnapshot`) plus the pure `ClusterGraphBuilder` domain service and the `IKafkaClusterReader` abstraction.
- **Kafdoc.Application** — `ISnapshotStore` (singleton holding the current immutable snapshot), `ClusterRefreshService` + `ClusterRefreshHostedService` (startup + interval refresh, FluentResults), and query services (`ITopicQueryService`/`IUserQueryService`/`ISnapshotStatusService`) mapping the graph to DTOs (Mapperly).
- **Kafdoc.Infrastructure** — `ConfluentKafkaClusterReader` over Confluent.Kafka's `IAdminClient`; `KafkaConnectionOptions`.
- **Kafdoc.Web** — Blazor Server pages reading the singleton snapshot directly (no DbContext, so no per-circuit scope dance).

Delete the `### Database access from Blazor components` subsection entirely (no database). Delete the EF/Npgsql references in `### Central management` / `## Conventions` and the `dotnet-ef` mention.

- [x] **Step 5: Build and run the full suite (excluding the Docker-dependent project if no daemon)**

Run: `dotnet build --no-restore -warnaserror`
Then: `dotnet test --no-restore` (runs all projects; `Kafdoc.InfrastructureTest` needs Docker).
Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add .devcontainer CLAUDE.md
git commit -m "chore: devcontainer Kafka broker and updated docs"
```

---

## Task 11: Full verification and end-to-end manual smoke

**Files:** none (verification only)

- [x] **Step 1: Clean build with warnings as errors**

Run: `dotnet build --no-restore -warnaserror`
Expected: PASS, zero warnings.

- [x] **Step 2: Run the full test suite**

Run: `dotnet test --no-restore`
Expected: All projects PASS (Domain, Application, Web, Architecture, Infrastructure integration).

- [ ] **Step 3: Manual smoke test against the devcontainer broker**

1. Ensure the devcontainer `kafka` service is up and seeded with at least one topic, ACL, SCRAM user, and a consumer group (per Task 10).
2. Set `Kafka:BootstrapServers=localhost:9092`, `Kafka:SecurityProtocol=SaslPlaintext`, `Kafka:SaslMechanism=ScramSha512`, `Kafka:SaslUsername=admin`, `Kafka:SaslPassword=<admin-secret>` via user-secrets:
   `cd src/Kafdoc.Web && dotnet user-secrets set "Kafka:BootstrapServers" "localhost:9092"` (repeat per key).
3. Run: `cd src/Kafdoc.Web && dotnet run`
4. Open the app, confirm the Topics list populates after the first refresh, a topic's detail shows producers/consumer groups, the Users page lists principals, and **Refresh now** updates the last-refreshed timestamp.

Expected: data visible end-to-end; no console errors.

- [x] **Step 4: Final commit (if any docs/tweaks from smoke testing)**

```bash
git add -A
git commit -m "chore: phase-1 verification fixes"
```

---

## Self-Review notes (for the implementer)

- **Spec coverage:** topics/ACLs/users/consumer-group reading (Task 7, 9), graph derivation incl. producer=WRITE/consumer=READ+offsets/user↔group bridge (Task 4), in-memory singleton + startup+interval refresh + stale-on-failure (Tasks 5–6), single-cluster config (Task 7), Blazor lists + topic detail + users + refresh-now + status (Task 8), EF/Postgres strip (Task 1), secured-broker Testcontainers tests (Task 9), devcontainer Kafka (Task 10), updated architecture tests (Task 1, 8). All spec sections map to a task.
- **Type consistency:** `RawClusterData`/`Raw*` (Task 2) are consumed unchanged by the builder (Task 4) and produced by the adapter (Task 7); `ClusterGraph` edge records (Task 3) are consumed by query services (Task 5). DTO and service names are identical across Tasks 5/6/8.
- **Highest-risk task:** Task 9 Step 3 (secured KRaft Testcontainer). Spike it in isolation first; the rest of the plan does not depend on its internals, only that `ReadAsync` returns the seeded `Raw*` records.
