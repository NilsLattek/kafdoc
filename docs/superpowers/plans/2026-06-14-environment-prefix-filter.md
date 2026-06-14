# Environment Prefix Filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restrict the documented Kafka cluster to a single logical environment by keeping only topics, users, and consumer groups whose names match configurable prefixes.

**Architecture:** A new pure domain service, `RawClusterDataFilter`, transforms `RawClusterData` between the reader and the builder (reader → **filter** → builder). Because the snapshot is reduced before the graph is built, every query, edge, and page is automatically scoped with no per-query logic. The filter is an allow-list keyed by prefix, configured per resource type via the new `Kafka:Filter` section; empty lists keep everything (opt-in).

**Tech Stack:** C# / .NET 10, xUnit v3 (Microsoft.Testing.Platform), NSubstitute, Microsoft.Extensions.Options configuration binding.

**Spec:** `docs/superpowers/specs/2026-06-14-environment-prefix-filter-design.md`

---

## File Structure

- **Create** `src/Kafdoc.Domain/Kafka/ClusterFilterOptions.cs` — options object (three prefix lists) bound from `Kafka:Filter`.
- **Create** `src/Kafdoc.Domain/Kafka/RawClusterDataFilter.cs` — pure transform reducing `RawClusterData`.
- **Modify** `src/Kafdoc.Domain/Kafdoc.Domain.csproj` — add the options-binding package.
- **Modify** `src/Kafdoc.Domain/Configuration.cs` — bind options + register the filter.
- **Modify** `src/Kafdoc.Application/Snapshot/ClusterRefreshService.cs` — inject and apply the filter.
- **Create** `test/Kafdoc.DomainTest/Kafka/RawClusterDataFilterTests.cs` — unit tests for the filter.
- **Modify** `test/Kafdoc.ApplicationTest/Snapshot/ClusterRefreshServiceTests.cs` — update constructor calls + add a wiring test.
- **Modify** `src/Kafdoc.Web/appsettings.json` — add an empty `Filter` example section.

All `dotnet` commands below assume the repo root `/workspaces/kafdoc` as the working directory. Use `--no-restore` after the first `dotnet restore`.

---

### Task 1: `ClusterFilterOptions`

**Files:**
- Create: `src/Kafdoc.Domain/Kafka/ClusterFilterOptions.cs`

- [ ] **Step 1: Create the options class**

Create `src/Kafdoc.Domain/Kafka/ClusterFilterOptions.cs`:

```csharp
namespace Kafdoc.Domain.Kafka;

/// <summary>
/// Prefix allow-lists that restrict the documented cluster to a single logical
/// environment. An empty list for a resource type keeps every resource of that type.
/// </summary>
public sealed class ClusterFilterOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Kafka:Filter";

    /// <summary>Name prefixes of topics to keep. Empty keeps all topics.</summary>
    public IReadOnlyList<string> TopicPrefixes { get; set; } = [];

    /// <summary>
    /// Principal-name prefixes of users to keep, matched after the <c>User:</c>
    /// type prefix is stripped. Empty keeps all users.
    /// </summary>
    public IReadOnlyList<string> UserPrefixes { get; set; } = [];

    /// <summary>Name prefixes of consumer groups to keep. Empty keeps all groups.</summary>
    public IReadOnlyList<string> GroupPrefixes { get; set; } = [];
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build --no-restore -warnaserror src/Kafdoc.Domain`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/Kafdoc.Domain/Kafka/ClusterFilterOptions.cs
git commit -m "Add ClusterFilterOptions for environment prefix filtering"
```

---

### Task 2: `RawClusterDataFilter`

**Files:**
- Create: `src/Kafdoc.Domain/Kafka/RawClusterDataFilter.cs`
- Test: `test/Kafdoc.DomainTest/Kafka/RawClusterDataFilterTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `test/Kafdoc.DomainTest/Kafka/RawClusterDataFilterTests.cs`:

```csharp
using Kafdoc.Domain.Graph;
using Kafdoc.Domain.Kafka;

namespace Kafdoc.DomainTest.Kafka;

public class RawClusterDataFilterTests
{
    private static RawClusterData Raw(
        IReadOnlyList<RawTopic>? topics = null,
        IReadOnlyList<RawAcl>? acls = null,
        IReadOnlyList<RawConsumerGroup>? groups = null,
        IReadOnlyList<RawScramUser>? scram = null) =>
        new(topics ?? [], acls ?? [], groups ?? [], scram ?? []);

    private static RawAcl Acl(string principal, string resource) =>
        new(principal, KafkaResourceType.Topic, resource,
            KafkaResourcePatternType.Literal, KafkaAclOperation.Write, KafkaAclPermission.Allow);

    [Fact]
    public void Apply_keeps_topics_matching_a_prefix()
    {
        // Arrange
        var filter = new RawClusterDataFilter(new ClusterFilterOptions { TopicPrefixes = ["qa."] });
        var raw = Raw(topics: [new RawTopic("qa.orders", 1), new RawTopic("dev.orders", 1)]);

        // Act
        var result = filter.Apply(raw);

        // Assert
        Assert.Contains(result.Topics, t => t.Name == "qa.orders");
        Assert.DoesNotContain(result.Topics, t => t.Name == "dev.orders");
    }

    [Fact]
    public void Apply_with_empty_topic_prefixes_keeps_all_topics()
    {
        // Arrange
        var filter = new RawClusterDataFilter(new ClusterFilterOptions());
        var raw = Raw(topics: [new RawTopic("qa.orders", 1), new RawTopic("dev.orders", 1)]);

        // Act
        var result = filter.Apply(raw);

        // Assert
        Assert.Equal(2, result.Topics.Count);
    }

    [Fact]
    public void Apply_keeps_users_matching_prefix_after_stripping_user_type()
    {
        // Arrange
        var filter = new RawClusterDataFilter(new ClusterFilterOptions { UserPrefixes = ["qa-"] });
        var raw = Raw(scram: [new RawScramUser("User:qa-svc"), new RawScramUser("User:dev-svc")]);

        // Act
        var result = filter.Apply(raw);

        // Assert
        Assert.Contains(result.ScramUsers, u => u.Principal == "User:qa-svc");
        Assert.DoesNotContain(result.ScramUsers, u => u.Principal == "User:dev-svc");
    }

    [Fact]
    public void Apply_keeps_acls_whose_principal_matches_user_prefix()
    {
        // Arrange
        var filter = new RawClusterDataFilter(new ClusterFilterOptions { UserPrefixes = ["qa-"] });
        var raw = Raw(acls: [Acl("User:qa-svc", "qa.orders"), Acl("User:dev-svc", "dev.orders")]);

        // Act
        var result = filter.Apply(raw);

        // Assert
        Assert.Contains(result.Acls, a => a.Principal == "User:qa-svc");
        Assert.DoesNotContain(result.Acls, a => a.Principal == "User:dev-svc");
    }

    [Fact]
    public void Apply_keeps_groups_matching_prefix_and_projects_consumed_topics()
    {
        // Arrange
        var filter = new RawClusterDataFilter(new ClusterFilterOptions
        {
            TopicPrefixes = ["qa."],
            GroupPrefixes = ["qa."],
        });
        var raw = Raw(groups:
        [
            new RawConsumerGroup("qa.readers", "Stable", 1, ["qa.orders", "dev.orders"]),
            new RawConsumerGroup("dev.readers", "Stable", 1, ["dev.orders"]),
        ]);

        // Act
        var result = filter.Apply(raw);

        // Assert
        var group = Assert.Single(result.ConsumerGroups);
        Assert.Equal("qa.readers", group.GroupId);
        Assert.Equal(["qa.orders"], group.ConsumedTopics);
    }

    [Fact]
    public void Apply_with_all_prefix_lists_empty_returns_equivalent_data()
    {
        // Arrange
        var filter = new RawClusterDataFilter(new ClusterFilterOptions());
        var raw = Raw(
            topics: [new RawTopic("qa.orders", 1)],
            acls: [Acl("User:qa-svc", "qa.orders")],
            groups: [new RawConsumerGroup("qa.readers", "Stable", 1, ["qa.orders"])],
            scram: [new RawScramUser("User:qa-svc")]);

        // Act
        var result = filter.Apply(raw);

        // Assert
        Assert.Single(result.Topics);
        Assert.Single(result.Acls);
        Assert.Single(result.ConsumerGroups);
        Assert.Single(result.ScramUsers);
    }

    [Fact]
    public void Apply_throws_when_raw_is_null()
    {
        // Arrange
        var filter = new RawClusterDataFilter(new ClusterFilterOptions());

        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => filter.Apply(null!));
    }

    [Fact]
    public void Filtered_acl_referencing_a_dropped_topic_yields_no_edge()
    {
        // Arrange
        var filter = new RawClusterDataFilter(new ClusterFilterOptions
        {
            TopicPrefixes = ["qa."],
            UserPrefixes = ["qa-"],
        });
        var raw = Raw(
            topics: [new RawTopic("qa.orders", 1), new RawTopic("dev.orders", 1)],
            acls:
            [
                Acl("User:qa-svc", "qa.orders"),
                Acl("User:qa-svc", "dev.orders"),
            ]);

        // Act
        var graph = new ClusterGraphBuilder().Build(filter.Apply(raw));

        // Assert
        Assert.Contains(new ProducerEdge("User:qa-svc", "qa.orders"), graph.Producers);
        Assert.DoesNotContain(new ProducerEdge("User:qa-svc", "dev.orders"), graph.Producers);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --no-restore test/Kafdoc.DomainTest --filter-class "*RawClusterDataFilterTests*"`
Expected: FAIL — `RawClusterDataFilter` does not exist (compile error).

- [ ] **Step 3: Implement the filter**

Create `src/Kafdoc.Domain/Kafka/RawClusterDataFilter.cs`:

```csharp
namespace Kafdoc.Domain.Kafka;

/// <summary>
/// Reduces <see cref="RawClusterData"/> to a single logical environment by keeping
/// only resources whose names match the configured prefixes. Pure and deterministic:
/// it performs no I/O and depends only on its input and options.
/// </summary>
/// <param name="options">The prefix allow-lists per resource type.</param>
public sealed class RawClusterDataFilter(ClusterFilterOptions options)
{
    private const string PrincipalTypePrefix = "User:";

    /// <summary>
    /// Applies the configured prefix allow-lists, returning a reduced copy of the
    /// raw data. Resource-side ACL pruning is left to the graph builder, which only
    /// matches ACL resources against the surviving topic and group names.
    /// </summary>
    /// <param name="raw">The raw cluster facts to filter.</param>
    /// <returns>A reduced <see cref="RawClusterData"/>.</returns>
    public RawClusterData Apply(RawClusterData raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var topics = raw.Topics
            .Where(t => Matches(t.Name, options.TopicPrefixes))
            .ToList();

        var acls = raw.Acls
            .Where(a => Matches(PrincipalName(a.Principal), options.UserPrefixes))
            .ToList();

        var scramUsers = raw.ScramUsers
            .Where(u => Matches(PrincipalName(u.Principal), options.UserPrefixes))
            .ToList();

        var groups = raw.ConsumerGroups
            .Where(g => Matches(g.GroupId, options.GroupPrefixes))
            .Select(g => g with
            {
                ConsumedTopics = g.ConsumedTopics
                    .Where(t => Matches(t, options.TopicPrefixes))
                    .ToList(),
            })
            .ToList();

        return new RawClusterData(topics, acls, groups, scramUsers);
    }

    private static bool Matches(string value, IReadOnlyList<string> prefixes) =>
        prefixes.Count == 0
        || prefixes.Any(p => value.StartsWith(p, StringComparison.Ordinal));

    private static string PrincipalName(string principal) =>
        principal.StartsWith(PrincipalTypePrefix, StringComparison.Ordinal)
            ? principal[PrincipalTypePrefix.Length..]
            : principal;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --no-restore test/Kafdoc.DomainTest --filter-class "*RawClusterDataFilterTests*"`
Expected: PASS — all 8 tests green.

- [ ] **Step 5: Build with warnings-as-errors**

Run: `dotnet build --no-restore -warnaserror src/Kafdoc.Domain`
Expected: Build succeeded, 0 warnings. (`Apply` uses the `options` field, so no CA1822/S2325 static suggestion.)

- [ ] **Step 6: Commit**

```bash
git add src/Kafdoc.Domain/Kafka/RawClusterDataFilter.cs test/Kafdoc.DomainTest/Kafka/RawClusterDataFilterTests.cs
git commit -m "Add RawClusterDataFilter pure domain transform"
```

---

### Task 3: Register the filter in DI

**Files:**
- Modify: `src/Kafdoc.Domain/Kafdoc.Domain.csproj`
- Modify: `src/Kafdoc.Domain/Configuration.cs`

- [ ] **Step 1: Add the options-binding package to the Domain project**

In `src/Kafdoc.Domain/Kafdoc.Domain.csproj`, add the package reference to the first `<ItemGroup>` so it reads:

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Configuration" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" />
  </ItemGroup>
```

(The version is already pinned in `Directory.Packages.props`, so no version attribute is needed.)

- [ ] **Step 2: Bind options and register the filter**

Replace the body of `ConfigureDomain` in `src/Kafdoc.Domain/Configuration.cs`. The full file becomes:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Kafdoc.Domain.Kafka;

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
        services.AddOptions<ClusterFilterOptions>()
            .Bind(configuration.GetSection(ClusterFilterOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ClusterFilterOptions>>().Value);

        services.AddSingleton<RawClusterDataFilter>();
        services.AddSingleton<Kafdoc.Domain.Graph.ClusterGraphBuilder>();
    }
}
```

- [ ] **Step 3: Restore (new package) and build**

Run: `dotnet restore && dotnet build --no-restore -warnaserror src/Kafdoc.Domain`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 4: Run the architecture tests to confirm layering still holds**

Run: `dotnet test --no-restore test/Kafdoc.ArchitectureTest`
Expected: PASS — Domain still has no outbound dependency on other layers (the new package is a framework dependency, not a project layer).

- [ ] **Step 5: Commit**

```bash
git add src/Kafdoc.Domain/Kafdoc.Domain.csproj src/Kafdoc.Domain/Configuration.cs
git commit -m "Register RawClusterDataFilter and bind ClusterFilterOptions"
```

---

### Task 4: Wire the filter into the refresh pipeline

**Files:**
- Modify: `src/Kafdoc.Application/Snapshot/ClusterRefreshService.cs`
- Test: `test/Kafdoc.ApplicationTest/Snapshot/ClusterRefreshServiceTests.cs`

- [ ] **Step 1: Update the two existing refresh tests for the new constructor, and add a wiring test**

In `test/Kafdoc.ApplicationTest/Snapshot/ClusterRefreshServiceTests.cs`:

Update the constructor call in `RefreshAsync_stores_snapshot_stamped_with_current_time` from:

```csharp
        var service = new ClusterRefreshService(reader, new ClusterGraphBuilder(), store, time);
```

to:

```csharp
        var service = new ClusterRefreshService(
            reader, new RawClusterDataFilter(new ClusterFilterOptions()), new ClusterGraphBuilder(), store, time);
```

Update the constructor call in `RefreshAsync_keeps_previous_snapshot_when_read_fails` from:

```csharp
        var service = new ClusterRefreshService(
            reader, new ClusterGraphBuilder(), store, new FakeTimeProvider());
```

to:

```csharp
        var service = new ClusterRefreshService(
            reader, new RawClusterDataFilter(new ClusterFilterOptions()),
            new ClusterGraphBuilder(), store, new FakeTimeProvider());
```

Then add this new test method to the class (the file already has `using Kafdoc.Domain.Kafka;` and `using Kafdoc.Domain.Graph;`):

```csharp
    [Fact]
    public async Task RefreshAsync_applies_filter_before_building()
    {
        // Arrange
        var reader = Substitute.For<IKafkaClusterReader>();
        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(new RawClusterData(
            [new RawTopic("qa.orders", 1), new RawTopic("dev.orders", 1)],
            [], [], []));
        var filter = new RawClusterDataFilter(new ClusterFilterOptions { TopicPrefixes = ["qa."] });
        var store = new SnapshotStore();
        var service = new ClusterRefreshService(
            reader, filter, new ClusterGraphBuilder(), store, new FakeTimeProvider());

        // Act
        var result = await service.RefreshAsync(CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var topicNames = store.Current!.Graph.Topics.Select(t => t.Name).ToList();
        Assert.Contains("qa.orders", topicNames);
        Assert.DoesNotContain("dev.orders", topicNames);
    }
```

- [ ] **Step 2: Run the refresh tests to verify the new one fails**

Run: `dotnet test --no-restore test/Kafdoc.ApplicationTest --filter-class "*ClusterRefreshServiceTests*"`
Expected: FAIL — `ClusterRefreshService` constructor does not yet accept a `RawClusterDataFilter` (compile error).

- [ ] **Step 3: Inject and apply the filter in `ClusterRefreshService`**

In `src/Kafdoc.Application/Snapshot/ClusterRefreshService.cs`, add `RawClusterDataFilter filter` to the primary constructor and apply it. The constructor and `RefreshAsync` head become:

```csharp
internal sealed class ClusterRefreshService(
    IKafkaClusterReader reader,
    RawClusterDataFilter filter,
    ClusterGraphBuilder builder,
    ISnapshotStore store,
    TimeProvider timeProvider) : IClusterRefreshService
{
    /// <inheritdoc />
    public async Task<Result> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var raw = filter.Apply(await reader.ReadAsync(cancellationToken).ConfigureAwait(false));
            var graph = builder.Build(raw);
            store.SetSnapshot(new ClusterSnapshot(graph, timeProvider.GetUtcNow()));
            return Result.Ok();
        }
```

Leave the rest of the method (the `catch` blocks) unchanged. The file already imports `Kafdoc.Domain.Kafka`, so `RawClusterDataFilter` resolves.

- [ ] **Step 4: Run the refresh tests to verify they pass**

Run: `dotnet test --no-restore test/Kafdoc.ApplicationTest --filter-class "*ClusterRefreshServiceTests*"`
Expected: PASS — all three tests green.

- [ ] **Step 5: Commit**

```bash
git add src/Kafdoc.Application/Snapshot/ClusterRefreshService.cs test/Kafdoc.ApplicationTest/Snapshot/ClusterRefreshServiceTests.cs
git commit -m "Apply RawClusterDataFilter in the refresh pipeline"
```

---

### Task 5: Document the config and full verification

**Files:**
- Modify: `src/Kafdoc.Web/appsettings.json`

- [ ] **Step 1: Add an empty `Filter` example to appsettings**

In `src/Kafdoc.Web/appsettings.json`, add a `Filter` object at the end of the `Kafka` section. The `Kafka` block becomes:

```jsonc
  "Kafka": {
    "BootstrapServers": "",
    "SecurityProtocol": "SaslSsl",
    "SaslMechanism": "ScramSha512",
    "SaslUsername": "",
    "SaslPassword": "",
    "RequestTimeout": "00:00:30",
    "RefreshInterval": "01:00:00",
    "Filter": {
      "TopicPrefixes": [],
      "UserPrefixes": [],
      "GroupPrefixes": []
    }
  }
```

(Empty arrays keep every resource, preserving current behaviour. Operators set e.g. `"TopicPrefixes": [ "qa." ]` to focus on one environment.)

- [ ] **Step 2: Confirm the JSON is valid and binds**

Run: `dotnet build --no-restore -warnaserror src/Kafdoc.Web`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Run the full solution build and test suite**

Run: `dotnet build --no-restore -warnaserror && dotnet test --no-restore`
Expected: Build succeeded with 0 warnings; all tests pass (Domain, Application, Architecture, Web, and Infrastructure — Infrastructure integration tests need a Docker daemon, available in the devcontainer).

- [ ] **Step 4: Commit**

```bash
git add src/Kafdoc.Web/appsettings.json
git commit -m "Document Kafka:Filter configuration section"
```

---

## Notes for the implementer

- **Why ACLs are filtered only by principal:** an ACL's resource side needs no explicit pruning. `ClusterGraphBuilder.MatchResources` (in `src/Kafdoc.Domain/Graph/ClusterGraphBuilder.cs`) only matches an ACL's resource against the *surviving* topic/group names, so a kept QA ACL referencing a now-absent dev topic produces no edge. Filtering ACLs by principal also keeps the builder's user list (derived from `acl.Principal` + SCRAM users) scoped to the chosen environment.
- **`User:` stripping:** both ACL principals and SCRAM users are stored as `User:<name>` (see `ConfluentKafkaClusterReader`), so user prefixes are matched against the name after `User:`. Configure `qa-`, not `User:qa-`.
- **Display names are not changed** anywhere — the filter only drops records; the Blazor pages render the surviving full names unchanged.
- **Run a single test method** with `dotnet test --no-restore --filter-method "*RawClusterDataFilter*"` if you need to isolate one case.
