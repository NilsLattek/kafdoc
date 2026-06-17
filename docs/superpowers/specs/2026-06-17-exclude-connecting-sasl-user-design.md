# Design: Ignore the connecting SASL user in the cluster graph

Date: 2026-06-17

## Problem

Kafdoc connects to Kafka with the principal configured in `Kafka:SaslUsername`.
That account is granted READ on (typically) all topics so the tool can read
metadata, ACLs, and offsets. As a consequence:

- `ClusterGraphBuilder` derives a `ConsumerEdge` from this principal to **every**
  topic (consumers come from READ ACLs).
- The principal's SCRAM entry produces a `KafkaUser` node.

The net effect is that Kafdoc's own service account shows up as a consumer on
every topic in the UI. This is noise that obscures the real producer/consumer
graph and should be removed.

## Goal

Automatically exclude the connecting SASL user (the principal in
`Kafka:SaslUsername`) from the documented cluster graph, with no extra
configuration required from the operator.

## Approach

Extend the existing `RawClusterDataFilter` (Domain), which already prunes `Acls`
and `ScramUsers` by the prefix allow-lists. Excluding a principal there removes
everything in one pure, well-tested place:

- Dropping the principal's `Acls` removes all of its producer/consumer/group
  edges (the builder derives edges only from surviving ACLs).
- Dropping the principal's `ScramUsers` entry removes the `KafkaUser` node.

The `ClusterGraphBuilder` stays a pure derivation step and needs no change.

### Rejected alternatives

- **Filter inside `ClusterGraphBuilder`** — the builder should remain pure
  derivation, not carry exclusion policy.
- **Post-prune the assembled `ClusterGraph` in Application** — more surfaces to
  touch (node and the several edge collections handled separately), more
  error-prone.

## Changes

1. **`ClusterFilterOptions`** (`Kafdoc.Domain/Kafka/ClusterFilterOptions.cs`)
   Add `IReadOnlyList<string> ExcludedUsers { get; set; } = []` — a deny-list of
   principal names matched after stripping the `User:` type prefix, exact ordinal
   match. A list (rather than a single string) stays consistent with the existing
   prefix lists and leaves room to add more excluded principals later. Empty list
   excludes nobody.

2. **`RawClusterDataFilter.Apply`** (`Kafdoc.Domain/Kafka/RawClusterDataFilter.cs`)
   After the existing prefix allow-list filtering, also drop any `Acls` and
   `ScramUsers` whose stripped principal name appears in `ExcludedUsers`. A
   resource is kept when it passes the allow-list **and** is not excluded. Reuse
   the existing `PrincipalName` helper so both bare (`svc`) and prefixed
   (`User:svc`) forms match.

3. **Wiring** (`Kafdoc.Domain/Configuration.cs`)
   `PostConfigure<ClusterFilterOptions>` reads `configuration["Kafka:SaslUsername"]`
   and, when non-empty/non-whitespace, appends it to `ExcludedUsers`. This makes
   the exclusion automatic — no new appsettings keys and no operator action.
   Domain already binds the `Kafka:Filter` section from configuration, so reading
   one additional `Kafka:` string key adds no type dependency on Infrastructure
   and does not violate the layering rules.

## Edge cases

- Empty or whitespace `SaslUsername` → no exclusion; behavior unchanged.
- Principal stored as `User:<name>` in ACLs/SCRAM vs. the bare `<name>` in
  `SaslUsername` → handled by the existing `PrincipalName` stripping helper.
- A different, legitimately documented user whose name merely *starts with* the
  SASL username → unaffected; exclusion is exact match, not prefix.

## Testing

Add to `RawClusterDataFilterTests` (`Kafdoc.DomainTest`):

- Excluded user's ACLs are dropped.
- Excluded user's SCRAM entry is dropped.
- A non-excluded user is retained (and one whose name shares a prefix with the
  excluded user survives, proving exact match).
- Empty `ExcludedUsers` keeps everything (regression guard).

The `Kafka:SaslUsername` → `ExcludedUsers` wiring is straightforward DI
composition and is covered by the filter behavior tests above; no separate
container test is required.
