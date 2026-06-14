# Environment Prefix Filter — Design

**Date:** 2026-06-14
**Status:** Approved

## Problem

Kafdoc currently documents every topic, user, consumer group, and ACL it finds in
the configured Kafka cluster. When that cluster is a shared non-production broker
hosting two logical environments (e.g. `dev` and `qa`), each topic and user exists
twice, distinguished only by a naming prefix (`dev.orders` / `qa.orders`,
`User:dev-svc` / `User:qa-svc`). The UI becomes noisy and it is hard to focus on a
single environment.

We need a **configurable** way to restrict the documented cluster to a chosen
environment by matching name prefixes. Filtering topics, users, and consumer groups
should transitively filter their ACLs and edges so the graph only describes the
selected environment.

## Goals

- Keep only the resources whose names match configured prefixes (an allow-list).
- Separate prefix configuration per resource type (topics, users, consumer groups),
  because naming schemes differ between types.
- Opt-in: an empty or missing configuration keeps everything, so existing
  single-environment deployments are unaffected.
- Display full, unmodified names (no prefix stripping).

## Non-Goals

- Runtime switching between environments without a restart. Filtering is applied at
  refresh time from configuration; changing it requires a config change + restart.
- Regex / glob matching. Prefix matching only.
- Deny-list semantics. Allow-list (keep-by-prefix) only.

## Decisions

| Question | Decision |
| --- | --- |
| Match mechanism | Keep by prefix (allow-list). |
| Scope | Separate prefix lists per resource type: topics, users, groups. |
| Display names | Show full names; no prefix stripping. |
| Empty/missing prefix list | Keep all resources of that type (opt-in feature). |
| User prefix matching | Match against the principal name **after** stripping the `User:` type prefix. |
| Where filtering happens | A pure domain service transforms `RawClusterData` between reader and builder. |

## Architecture

The codebase already follows "raw facts → pure transform → read model". The filter
slots cleanly into that grain as a second pure transform:

```
IKafkaClusterReader.ReadAsync()  ->  RawClusterDataFilter.Apply()  ->  ClusterGraphBuilder.Build()  ->  ISnapshotStore
        (Infrastructure)                  (Domain, new)                      (Domain)                     (Application)
```

Because filtering produces a reduced `RawClusterData` *before* the graph is built,
the existing builder is unchanged and every downstream query, edge, and Blazor page
is automatically scoped to the selected environment. No per-query filtering logic is
needed anywhere.

### Considered alternatives

- **Filter inside the Infrastructure reader.** Rejected: it couples filtering policy
  to the Kafka adapter, which deliberately contains no derivation logic and is only
  integration-tested. Harder to unit-test.
- **Filter at the query/UI layer.** Rejected: the snapshot would still hold both
  environments, and every query would have to re-filter users, ACLs, and edges.
  Most code for a config-driven, restart-level need.

## Components

### `ClusterFilterOptions` (Domain, new)

Plain options object bound from the `Kafka:Filter` configuration section.

```csharp
public sealed class ClusterFilterOptions
{
    public const string SectionName = "Kafka:Filter";

    /// <summary>Name prefixes of topics to keep. Empty keeps all topics.</summary>
    public IReadOnlyList<string> TopicPrefixes { get; set; } = [];

    /// <summary>Principal-name prefixes of users to keep (matched after the
    /// <c>User:</c> type prefix is stripped). Empty keeps all users.</summary>
    public IReadOnlyList<string> UserPrefixes { get; set; } = [];

    /// <summary>Name prefixes of consumer groups to keep. Empty keeps all groups.</summary>
    public IReadOnlyList<string> GroupPrefixes { get; set; } = [];
}
```

### `RawClusterDataFilter` (Domain, new)

Pure domain service, primary constructor taking `ClusterFilterOptions`, registered as
a singleton like `ClusterGraphBuilder`. Performs no I/O.

```csharp
RawClusterData Apply(RawClusterData raw);
```

Filtering rules (all matching uses `StringComparison.Ordinal`):

- **Topics** — keep each `RawTopic` whose `Name` starts with any `TopicPrefix`.
- **Consumer groups** — keep each `RawConsumerGroup` whose `GroupId` starts with any
  `GroupPrefix`. For each surviving group, project its `ConsumedTopics` down to those
  matching the topic prefixes, so a group→topic edge cannot dangle into the other
  environment.
- **SCRAM users** — keep each `RawScramUser` whose principal name (the part after the
  `User:` type prefix) starts with any `UserPrefix`.
- **ACLs** — keep each `RawAcl` whose principal name (after `User:`) starts with any
  `UserPrefix`. The resource side needs no explicit work: the existing
  `ClusterGraphBuilder.MatchResources` only matches ACL resources against the
  *surviving* topic/group names, so a kept QA ACL that references an absent dev topic
  simply produces no edge.
- **Empty prefix list** for a type → keep all resources of that type.

Principal-name helper: given a principal like `User:qa-svc`, strip a leading
`User:` (the only principal type Kafdoc reads) before prefix-matching; if the prefix
is absent, match the whole string.

### Wiring

`ClusterRefreshService` gains a `RawClusterDataFilter` dependency and applies it
between read and build:

```csharp
var raw = filter.Apply(await reader.ReadAsync(cancellationToken).ConfigureAwait(false));
var graph = builder.Build(raw);
```

`Domain.Configuration.ConfigureDomain` registers the filter and binds
`ClusterFilterOptions` from `Kafka:Filter`.

## Configuration example

```jsonc
"Kafka": {
  "BootstrapServers": "broker:9092",
  // ...existing connection settings...
  "Filter": {
    "TopicPrefixes": [ "qa." ],
    "UserPrefixes":  [ "qa-" ],
    "GroupPrefixes": [ "qa." ]
  }
}
```

Omitting `Kafka:Filter` (or any individual list) keeps all resources of that type.

## Error handling

The filter is a pure, total transform that cannot fail on valid input; it never
throws on empty inputs. It guards its `RawClusterData` argument with
`ArgumentNullException.ThrowIfNull`, matching `ClusterGraphBuilder`. Any unexpected
exception during refresh is already caught by `ClusterRefreshService` and surfaced as
a `Result.Fail`, leaving the previous snapshot intact (stale-but-serving).

## Testing

`RawClusterDataFilterTests` in `Kafdoc.DomainTest` (xUnit v3, snake_case names,
Arrange/Act/Assert):

- `Apply_keeps_topics_matching_a_prefix`
- `Apply_drops_topics_not_matching_any_prefix`
- `Apply_with_empty_topic_prefixes_keeps_all_topics`
- `Apply_keeps_users_matching_prefix_after_stripping_user_type`
- `Apply_keeps_acls_whose_principal_matches_user_prefix`
- `Apply_drops_acls_whose_principal_does_not_match`
- `Apply_projects_consumed_topics_to_surviving_topic_prefixes`
- `Apply_with_all_prefix_lists_empty_returns_equivalent_data`

Plus a builder-level check that a kept ACL referencing a dropped topic yields no
producer/consumer edge (confirming the "ACLs filtered automatically" behaviour
end-to-end through filter + builder).

## Code style

XML comments on all new public types/members; primary constructor on the filter;
`StringComparison.Ordinal` throughout; central package management (no new packages
expected). Register the new service in `Domain.Configuration`, not `Program.cs`.
