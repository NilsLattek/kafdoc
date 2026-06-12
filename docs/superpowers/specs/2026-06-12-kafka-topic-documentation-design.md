# Kafka Topic Documentation — Design

**Date:** 2026-06-12
**Status:** Approved (phase 1)

## Overview

Kafdoc connects to a single secured Kafka cluster, reads its topics, ACLs, users,
and consumer groups via the Kafka Admin API, and builds an in-memory **graph** that
shows which users (principals) produce to and consume from which topics, and which
consumer groups (the "applications") actually consume them. The graph is rebuilt
from the cluster on app startup and refreshed on a regular interval (default hourly).
A Blazor Server frontend presents the data as searchable lists with per-topic detail.

This document covers **phase 1**: fetching all data from Kafka and presenting it.
Later phases (out of scope here) add markdown-based enrichment, interactive graph
visualization, and multi-cluster support.

## Goals

- Connect to one SASL-secured, ACL-enabled Kafka cluster using the Admin API.
- Read topics, ACLs, users (principals + SCRAM credentials), and consumer groups.
- Derive a producer/consumer graph and keep it in memory.
- Refresh on startup and on a configurable interval; serve the last good snapshot
  while a refresh is in progress or after a failed refresh.
- Present topics, their producers, and their consumers in a Blazor Server UI.
- Test the graph derivation thoroughly (unit) and the Kafka adapter against a real
  secured broker (integration, via Testcontainers).

## Non-goals (later phases)

- Markdown enrichment (filesystem docs matched to users and topics).
- Interactive node-link graph visualization.
- Multiple clusters.
- Consumer-group lag metrics.
- Snapshot history / persistence across restarts.

## Background: what Kafka does and does not expose

The data model is shaped entirely by what the broker actually knows:

- **Topics** and **partition counts** are directly listable.
- **Consumer groups** are tracked by the group coordinator: state, members,
  assignments, and committed offsets. Committed offsets reveal the concrete topics a
  group actually consumes.
- **Consumer-group metadata does NOT expose the authenticated principal** of its
  members — only `client.id`, `consumer.id`, and host.
- **Producers are not tracked at all.** The broker keeps no registry of who produces
  to a topic. The only broker-side signal for "this principal writes to this topic"
  is a `WRITE` ACL.
- **Users** exist only on a secured cluster: as ACL principals and/or SCRAM
  credentials.

Consequences encoded in the design:

- Producer edges rest **entirely** on `WRITE` ACLs (a permission signal, subject to
  over-provisioning — there is no runtime truth for producers).
- Consumer edges combine two signals: `READ` ACLs (permitted) and consumer-group
  offsets (actual consumption).
- The bridge from a consumer group back to a **user** is the `READ` ACL on the
  **group resource** (`Group:<group.id>`, principal `<user>`), since group metadata
  lacks the principal.

## Architecture

The existing four-project layering is kept (Web → Application → Domain ← Infrastructure),
but the EF Core / Postgres stack is removed and Infrastructure is repurposed as the
Kafka adapter. The data is a **read model**, not a mutable aggregate domain: the real
domain logic is deriving the graph from raw Kafka facts, and that is what gets modeled
and unit-tested.

### Removed from the template

- EF Core, Npgsql, `EFCore.NamingConventions`, `EntityFrameworkCore.Exceptions.PostgreSQL`.
- Ardalis.Specification and the `IRepository<T>` / `IUnitOfWork` abstractions and
  `Specifications/`.
- The Postgres `db` service in the devcontainer and `dotnet-ef` install.
- The `WithNewScopeAsync` DbContext-scope guidance (no DbContext ⇒ no per-circuit
  scope problem; query services inject directly into components).
- Placeholder stubs: `Dummy.cs`, `ExampleMapper` commented body, old Chart/OrgNode
  tests and pages (Counter, Weather).

### Kept from the template

- Four-project layering and `ConfigureXxx` DI extension pattern.
- FluentResults for application-service results.
- Mapperly for entity→DTO mapping.
- Analyzer gatekeepers (Meziantou, SonarAnalyzer, Roslynator) and `-warnaserror` CI.
- Test conventions: xUnit v3, Microsoft.Testing.Platform, NSubstitute, snake_case
  test names, ArchUnit architecture tests.

### Kafdoc.Domain — read model + graph builder

Immutable records:

- `KafkaTopic` — name, partition count.
- `KafkaUser` — principal (e.g. `User:svc-payments`), and whether it has SCRAM creds.
- `KafkaConsumerGroup` — group id, state, the topics it consumes (from offsets),
  member count.
- `KafkaAcl` — principal, resource type, resource name, resource pattern type
  (LITERAL / PREFIXED), operation, permission type, host.
- `ClusterGraph` — the assembled nodes (topics, users, groups) and typed edges.
- `ClusterSnapshot` — `ClusterGraph` + `CapturedAt` timestamp.

Edge types and their derivation:

| Edge | Meaning | Derived from |
|------|---------|--------------|
| User → Topic (produces) | principal may write | `WRITE` ACL on the topic |
| User → Topic (consumes) | principal may read | `READ` ACL on the topic |
| User → ConsumerGroup | principal backs the group | `READ` ACL on the group resource |
| ConsumerGroup → Topic (consumes) | group actually consuming | committed offsets / assignments |

Abstractions and services:

- `IKafkaClusterReader` (Domain abstraction, implemented in Infrastructure) returning
  raw facts as a `RawClusterData` record (raw topics, raw ACL bindings, raw consumer
  groups with offsets, raw SCRAM users).
- `ClusterGraphBuilder` (domain service): a pure function `RawClusterData → ClusterGraph`.
  Responsibilities:
  - Union all principals from ACLs with SCRAM users into the `KafkaUser` set
    (a principal may have ACLs without SCRAM creds, e.g. mTLS principals).
  - Resolve `LITERAL` and `PREFIXED` ACL resource patterns against the actual topic
    list to produce concrete topic edges (and `*` wildcards).
  - Build group → topic edges from committed offsets.
  - Build user ↔ group edges from group-resource `READ` ACLs.
  - Flag orphans: topics/groups present in metadata with no matching ACL, and ACLs
    referencing resources that do not exist.

### Kafdoc.Application — orchestration + query API

- `ISnapshotStore` (singleton): holds the current `ClusterSnapshot` behind a
  `volatile` reference, swapped atomically. Reads are lock-free and thread-safe.
  Also exposes status: `LastRefresh`, `LastError`, `IsReady`.
- `ClusterRefreshService.RefreshAsync` — calls the reader, runs the builder, swaps the
  snapshot into the store. Returns FluentResults; exceptions (except
  `OperationCanceledException`) become `Result.Fail` and **leave the previous snapshot
  intact** (stale-but-serving beats empty).
- Query services for the UI (e.g. `ITopicQueryService`, `IUserQueryService`) returning
  DTOs mapped via Mapperly. These read the singleton snapshot directly.

### Kafdoc.Infrastructure — Kafka adapter

- `ConfluentKafkaClusterReader : IKafkaClusterReader`, over `Confluent.Kafka`'s
  `IAdminClient`. Gathers raw facts (in parallel where the client allows) and maps
  Confluent types → domain raw records:
  - Topics + partitions: `DescribeTopicsAsync` / metadata.
  - ACLs: `DescribeAclsAsync` with an empty filter (all bindings).
  - Consumer groups: `ListConsumerGroupsAsync` → `DescribeConsumerGroupsAsync` →
    `ListConsumerGroupOffsetsAsync`.
  - SCRAM users: `DescribeUserScramCredentialsAsync`.
- The adapter contains **no derivation logic** — only fetching and mapping — so the
  builder stays broker-free and unit-testable.
- `KafkaOptions` bound from configuration (see below); the `IAdminClient` is built
  from these options and registered in `ConfigureInfrastructure`.

### Kafdoc.Web — Blazor Server

- `ClusterRefreshHostedService` (`BackgroundService`): runs one refresh on startup,
  then loops on a `PeriodicTimer` at `KafkaOptions.RefreshInterval`. Logs failures;
  never tears down the last good snapshot.
- Pages (replacing the old Chart/Counter/Weather pages):
  - **Topics** (route `/`): searchable table — topic name, partitions, #producers,
    #consumer groups.
  - **Topic detail**: Producers (WRITE-ACL principals) and Consumers (consumer groups
    with their backing principal via group-resource READ ACL, plus READ-only
    principals not tied to an active group).
  - **Users** (optional, low cost): principal → topics produced/consumed + groups backed.
  - A header showing last-refresh time, status, and a **Refresh now** button that
    triggers an out-of-band `RefreshAsync`.
- Query services inject directly into components (singleton snapshot is thread-safe).
- Until the first snapshot lands, the UI shows a loading state.

## Configuration

`appsettings.json` `Kafka` section, bound to `KafkaOptions`:

| Key | Default | Notes |
|-----|---------|-------|
| `BootstrapServers` | — | required |
| `SecurityProtocol` | `SaslSsl` | |
| `SaslMechanism` | `ScramSha512` | |
| `SaslUsername` | — | secret (env/user-secrets) |
| `SaslPassword` | — | secret (env/user-secrets) |
| `SslCaLocation` | — | optional CA bundle path |
| `RefreshInterval` | `01:00:00` | `TimeSpan` |
| `RequestTimeout` | `00:00:30` | `TimeSpan` |

Secrets are never committed; supplied via environment variables or user-secrets in dev.

## Refresh lifecycle and failure handling

1. On startup the hosted service calls `RefreshAsync` once before serving "ready".
2. It then loops on a `PeriodicTimer` at `RefreshInterval`.
3. Each cycle: reader → builder → atomic swap into `ISnapshotStore`.
4. On failure: log, record `LastError`, keep the previous snapshot. The UI surfaces
   the staleness via the last-refresh timestamp and error indicator.
5. A manual **Refresh now** action invokes the same `RefreshAsync` out of band.

## Testing strategy

- **Unit tests (`Kafdoc.DomainTest`)** — the bulk of coverage. Feed fabricated
  `RawClusterData` into `ClusterGraphBuilder` and assert:
  - `WRITE` ACL ⇒ producer edge; `READ` ACL ⇒ consumer (permitted) edge.
  - `READ` ACL on a group resource bridges user ↔ group.
  - Committed offsets ⇒ group → topic consumption edges.
  - `LITERAL`, `PREFIXED`, and `*` ACL patterns resolve correctly against the topic list.
  - Principals with ACLs but no SCRAM creds still appear as users.
  - Orphan topics/groups and dangling ACLs are flagged.
- **Integration tests (new `Kafdoc.InfrastructureTest`)** — `Testcontainers` spins up a
  **secured KRaft broker** (SASL_PLAINTEXT + `StandardAuthorizer`). Seed topics, ACLs,
  a SCRAM user, and a consumer group, then assert `ConfluentKafkaClusterReader` reads
  them back and maps them to the expected raw records.
- **Architecture tests (`Kafdoc.ArchitectureTest`)** — updated for the new layering:
  drop EF/repository rules; keep layer-dependency and naming-convention rules.

Test conventions follow the repo: xUnit v3, Microsoft.Testing.Platform, NSubstitute,
snake_case behavior-describing names, Arrange/Act/Assert.

## Devcontainer

- Replace the Postgres `db` service in `.devcontainer/docker-compose.yml` with a
  **secured KRaft Kafka** service (SASL + `StandardAuthorizer`), seeded with sample
  topics/ACLs/users/groups so local `dotnet run` shows the real graph (a plaintext
  broker would hide the entire feature, since it has no ACLs or users).
- Update `network_mode` / forwarded ports accordingly.
- Drop the `dotnet-ef` `postCreateCommand`.
- **Prerequisite:** Testcontainers needs a Docker daemon reachable from inside the
  devcontainer (docker-outside-of-docker or equivalent). Flag in setup docs.

## Open follow-ups (future phases)

- Markdown enrichment for users (producer/consumer application docs) and topics (event
  docs), matched by name from the filesystem.
- Interactive node-link graph visualization.
- Multi-cluster support and cluster selection in the UI.
- Consumer-group lag and liveness metrics.
