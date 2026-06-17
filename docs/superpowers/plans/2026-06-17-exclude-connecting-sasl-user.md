# Exclude Connecting SASL User Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automatically exclude the connecting SASL user (the principal in `Kafka:SaslUsername`) from the documented cluster graph, so Kafdoc's own service account no longer appears as a consumer on every topic.

**Architecture:** Add an `ExcludedUsers` deny-list to `ClusterFilterOptions` and apply it in the existing pure `RawClusterDataFilter` (drops the principal's ACLs and SCRAM entry, which removes all its edges and its user node). Auto-populate the deny-list from `Kafka:SaslUsername` via `PostConfigure` in the Domain DI wiring — no new config keys, no operator action.

**Tech Stack:** C# / .NET 10, xUnit v3 (Microsoft.Testing.Platform runner), Microsoft.Extensions.Options / DI.

## Global Constraints

- Target framework `net10.0`; nullable enabled (from `Directory.Build.props`).
- CI builds with `-warnaserror`; Meziantou, SonarAnalyzer, Roslynator run on build. Treat warnings as errors locally.
- Central package management: no version changes needed (no new packages).
- Domain layer has **no outbound project dependencies**; this plan adds none (only reads an `IConfiguration` string key already available in `ConfigureDomain`).
- C# style: 4-space indent, XML comments on all public members, primary constructors, `StringComparison.Ordinal` for principal/name comparisons.
- Test naming: `<Method>_<Conditions>_<Outcome>` snake_case, Arrange/Act/Assert with section comments. No `Async` suffix.
- **Do NOT perform any git actions.** The repository owner reviews and commits. Where a step below says "Commit", instead STOP and report the completed work for the owner to review and commit.
- Test runner is Microsoft.Testing.Platform, not VSTest. Use the `dotnet test` filter flags shown.

---

### Task 1: Add `ExcludedUsers` deny-list and apply it in the filter

**Files:**
- Modify: `src/Kafdoc.Domain/Kafka/ClusterFilterOptions.cs`
- Modify: `src/Kafdoc.Domain/Kafka/RawClusterDataFilter.cs:28-34`
- Test: `test/Kafdoc.DomainTest/Kafka/RawClusterDataFilterTests.cs`

**Interfaces:**
- Consumes: existing `ClusterFilterOptions`, `RawClusterDataFilter.Apply(RawClusterData)`, and the private `PrincipalName(string)` / `Matches(string, IReadOnlyList<string>)` helpers.
- Produces: `ClusterFilterOptions.ExcludedUsers` (`IReadOnlyList<string>`, default `[]`) — a deny-list of principal names matched after stripping the `User:` prefix, exact ordinal match. Consumed by Task 2's wiring.

- [ ] **Step 1: Write the failing tests**

Add these four tests to `RawClusterDataFilterTests` (inside the class, alongside the existing tests). They reuse the existing `Raw(...)` and `Acl(...)` helpers.

```csharp
[Fact]
public void Apply_drops_acls_of_an_excluded_user()
{
    // Arrange
    var filter = new RawClusterDataFilter(new ClusterFilterOptions { ExcludedUsers = ["kafdoc-admin"] });
    var raw = Raw(acls: [Acl("User:kafdoc-admin", "qa.orders"), Acl("User:qa-svc", "qa.orders")]);

    // Act
    var result = filter.Apply(raw);

    // Assert
    Assert.DoesNotContain(result.Acls, a => string.Equals(a.Principal, "User:kafdoc-admin", StringComparison.Ordinal));
    Assert.Contains(result.Acls, a => string.Equals(a.Principal, "User:qa-svc", StringComparison.Ordinal));
}

[Fact]
public void Apply_drops_scram_entry_of_an_excluded_user()
{
    // Arrange
    var filter = new RawClusterDataFilter(new ClusterFilterOptions { ExcludedUsers = ["kafdoc-admin"] });
    var raw = Raw(scram: [new RawScramUser("User:kafdoc-admin"), new RawScramUser("User:qa-svc")]);

    // Act
    var result = filter.Apply(raw);

    // Assert
    Assert.DoesNotContain(result.ScramUsers, u => string.Equals(u.Principal, "User:kafdoc-admin", StringComparison.Ordinal));
    Assert.Contains(result.ScramUsers, u => string.Equals(u.Principal, "User:qa-svc", StringComparison.Ordinal));
}

[Fact]
public void Apply_excludes_user_by_exact_name_not_prefix()
{
    // Arrange
    var filter = new RawClusterDataFilter(new ClusterFilterOptions { ExcludedUsers = ["admin"] });
    var raw = Raw(scram: [new RawScramUser("User:admin"), new RawScramUser("User:admin-readonly")]);

    // Act
    var result = filter.Apply(raw);

    // Assert
    Assert.DoesNotContain(result.ScramUsers, u => string.Equals(u.Principal, "User:admin", StringComparison.Ordinal));
    Assert.Contains(result.ScramUsers, u => string.Equals(u.Principal, "User:admin-readonly", StringComparison.Ordinal));
}

[Fact]
public void Apply_with_empty_excluded_users_keeps_everyone()
{
    // Arrange
    var filter = new RawClusterDataFilter(new ClusterFilterOptions());
    var raw = Raw(
        acls: [Acl("User:kafdoc-admin", "qa.orders")],
        scram: [new RawScramUser("User:kafdoc-admin")]);

    // Act
    var result = filter.Apply(raw);

    // Assert
    Assert.Single(result.Acls);
    Assert.Single(result.ScramUsers);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --no-restore test/Kafdoc.DomainTest --filter-class "*RawClusterDataFilterTests*"`
Expected: FAIL — compile error, `ClusterFilterOptions` has no `ExcludedUsers` property.

- [ ] **Step 3: Add the `ExcludedUsers` property**

In `src/Kafdoc.Domain/Kafka/ClusterFilterOptions.cs`, add after the `GroupPrefixes` property (before the closing brace):

```csharp
    /// <summary>
    /// Principal names to exclude entirely, matched after the <c>User:</c> type
    /// prefix is stripped (exact, ordinal). Their ACLs and SCRAM entries are
    /// dropped, removing their nodes and edges from the graph. Empty excludes nobody.
    /// </summary>
    public IReadOnlyList<string> ExcludedUsers { get; set; } = [];
```

- [ ] **Step 4: Apply the exclusion in the filter**

In `src/Kafdoc.Domain/Kafka/RawClusterDataFilter.cs`, replace the `acls` and `scramUsers` assignments (lines 28-34) with:

```csharp
        var acls = raw.Acls
            .Where(a => Matches(PrincipalName(a.Principal), options.UserPrefixes))
            .Where(a => !IsExcluded(a.Principal))
            .ToList();

        var scramUsers = raw.ScramUsers
            .Where(u => Matches(PrincipalName(u.Principal), options.UserPrefixes))
            .Where(u => !IsExcluded(u.Principal))
            .ToList();
```

Then add this private helper next to the existing `Matches`/`PrincipalName` helpers (before the closing brace of the class):

```csharp
    private bool IsExcluded(string principal) =>
        options.ExcludedUsers.Contains(PrincipalName(principal), StringComparer.Ordinal);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --no-restore test/Kafdoc.DomainTest --filter-class "*RawClusterDataFilterTests*"`
Expected: PASS — all tests in the class green, including the four new ones.

- [ ] **Step 6: Build with warnings as errors**

Run: `dotnet build --no-restore -warnaserror src/Kafdoc.Domain`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 7: Commit**

(Per Global Constraints: do NOT run git. STOP and report Task 1 complete for the owner to review and commit.)

Intended message: `feat: support excluding principals from the cluster graph`

---

### Task 2: Auto-populate `ExcludedUsers` from `Kafka:SaslUsername`

**Files:**
- Modify: `src/Kafdoc.Domain/Configuration.cs:21-23`
- Test: manual verification (DI composition; behavior already covered by Task 1's filter tests).

**Interfaces:**
- Consumes: `ClusterFilterOptions.ExcludedUsers` (from Task 1); `IConfiguration` (already a parameter of `ConfigureDomain`).
- Produces: at runtime, `ClusterFilterOptions.ExcludedUsers` contains the configured `Kafka:SaslUsername` when it is non-empty/non-whitespace.

- [ ] **Step 1: Add the PostConfigure wiring**

In `src/Kafdoc.Domain/Configuration.cs`, replace the options registration block (lines 21-23):

```csharp
        services.AddOptions<ClusterFilterOptions>()
            .Bind(configuration.GetSection(ClusterFilterOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ClusterFilterOptions>>().Value);
```

with:

```csharp
        services.AddOptions<ClusterFilterOptions>()
            .Bind(configuration.GetSection(ClusterFilterOptions.SectionName))
            .PostConfigure(options =>
            {
                var saslUsername = configuration["Kafka:SaslUsername"];
                if (!string.IsNullOrWhiteSpace(saslUsername))
                {
                    options.ExcludedUsers = [.. options.ExcludedUsers, saslUsername];
                }
            });
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ClusterFilterOptions>>().Value);
```

The `using Microsoft.Extensions.Options;` import is already present in the file (used by the existing `IOptions<>` line), so no new import is required.

- [ ] **Step 2: Build with warnings as errors**

Run: `dotnet build --no-restore -warnaserror src/Kafdoc.Domain`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Run the full Domain test suite (regression)**

Run: `dotnet test --no-restore test/Kafdoc.DomainTest`
Expected: PASS — all tests green.

- [ ] **Step 4: Run the architecture tests (layering unchanged)**

Run: `dotnet test --no-restore test/Kafdoc.ArchitectureTest`
Expected: PASS — Domain still has no disallowed dependencies.

- [ ] **Step 5: Commit**

(Per Global Constraints: do NOT run git. STOP and report Task 2 complete for the owner to review and commit.)

Intended message: `feat: ignore the connecting SASL user in the cluster graph`

---

### Task 3: Full build and test sweep

**Files:** none (verification only).

- [ ] **Step 1: Restore and build the whole solution**

Run: `dotnet build --no-restore -warnaserror`
Expected: Build succeeded, 0 warnings across all projects.

- [ ] **Step 2: Run all non-integration tests**

Run: `dotnet test --no-restore test/Kafdoc.DomainTest test/Kafdoc.ApplicationTest test/Kafdoc.ArchitectureTest test/Kafdoc.WebTest`
Expected: PASS — all suites green.

(Skip `Kafdoc.InfrastructureTest` unless a Docker daemon is available; it spins up a real broker via Testcontainers and is unaffected by this change.)

- [ ] **Step 3: Report for review**

Summarize the change (deny-list in `ClusterFilterOptions`, applied in `RawClusterDataFilter`, auto-filled from `Kafka:SaslUsername`) and hand off to the owner to review and commit. Do NOT run git.

---

## Self-Review Notes

- **Spec coverage:** `ExcludedUsers` property (Task 1, Step 3) ✓; drop ACLs + SCRAM in `RawClusterDataFilter` (Task 1, Step 4) ✓; auto-populate from `Kafka:SaslUsername` via PostConfigure (Task 2) ✓; tests for ACL drop / SCRAM drop / exact-not-prefix / empty-list regression (Task 1, Step 1) ✓; edge cases empty username (Task 2 `IsNullOrWhiteSpace`) and `User:`-prefix stripping (reuses `PrincipalName`) ✓.
- **Placeholder scan:** none — all code shown in full.
- **Type consistency:** `ExcludedUsers` is `IReadOnlyList<string>` everywhere; `IsExcluded(string)` and `PrincipalName(string)` signatures consistent across tasks.
