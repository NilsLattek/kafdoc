# User Detail Page — Design

**Date:** 2026-06-15
**Status:** Approved

## Problem

The Users page (`/users`) lists every principal with counts of how many topics it
produces to and consumes from, but those counts are dead ends. A user cannot click a
principal to find out *which* topics it writes to (produces) and *which* topics it can
read from (consumes).

We want each principal on the Users page to be clickable, navigating to a detail page
that lists the user's produce and consume topics — mirroring the existing Topics →
Topic detail navigation.

## Goals

- Make each principal on `/users` a link to a per-user detail page.
- On that detail page, list the topics the user produces to and the topics it
  consumes, each as a link to the topic's own detail page (`/topics/{name}`).
- Match the existing app patterns (`TopicDetail.razor`, the Topics list link, empty
  and not-found states).

## Non-Goals

- No backend, DTO, or domain changes. The data already exists.
- Do **not** render the user's consumer groups on the detail page (kept focused on
  produce/consume). `UserDetailDto.Groups` remains in the DTO, simply unused here.
- No new graph queries, filtering, or counts.

## Decisions

| Question | Decision |
| --- | --- |
| How is the detail revealed? | A dedicated page at `/users/{Principal}`, mirroring `/topics/{Name}`. |
| Page content | Produces list + Consumes list; each topic is a link to `/topics/{name}`. |
| Consumer groups | Not shown. |
| Backend changes | None — `IUserQueryService.GetUser(principal)` already returns the needed data. |

## Architecture

This is a pure UI addition. The Application layer already exposes everything needed:

```csharp
UserDetailDto? IUserQueryService.GetUser(string principal);
// -> Principal, HasScramCredentials, ProducesTopics, ConsumesTopics, Groups
```

The new page calls `GetUser(Principal)` and renders `ProducesTopics` /
`ConsumesTopics`. No new service, DTO, or query is introduced. This mirrors
`TopicDetail.razor`, which calls `ITopicQueryService.GetTopic(Name)` the same way.

## Components

### `Components/Pages/UserDetail.razor` (Web, new)

Route `/users/{Principal}`, `@rendermode InteractiveServer`, injects
`IUserQueryService`. Structure mirrors `TopicDetail.razor`:

- Back link: `<a href="/users">&larr; All users</a>`.
- `var detail = UserQuery.GetUser(Principal);`
- If `detail is null`: render an `<h1>User not found</h1>` plus a note that no
  principal of that name exists in the current snapshot (same shape as TopicDetail's
  not-found block).
- Otherwise:
  - `<h1>@detail.Principal</h1>`
  - A line showing SCRAM credential status (`yes` / `—`), matching the Users list.
  - **Produces** section (`<h2>`): if `ProducesTopics` is empty, an `<em>` note
    ("No producer ACLs."); otherwise a `<ul>` where each topic is
    `<a href="@($"/topics/{Uri.EscapeDataString(topic)}")">@topic</a>`.
  - **Consumes** section (`<h2>`): same shape over `ConsumesTopics`
    ("No consumer ACLs." when empty).
- `@code` block: `[Parameter] public string Principal { get; set; } = string.Empty;`
  with an XML doc comment, matching `TopicDetail`'s `Name` parameter.

### `Components/Pages/Users.razor` (Web, edit)

In the table body, wrap the principal cell in a link, exactly as `Topics.razor` links
topic names:

```razor
<td><a href="@($"/users/{Uri.EscapeDataString(u.Principal)}")">@u.Principal</a></td>
```

No other change to the page (the loading state, SCRAM column, and counts stay).

## Routing note

Kafka principals look like `User:alice` and contain a colon. `Uri.EscapeDataString`
encodes the principal when building the link, and Blazor decodes the `{Principal}`
route parameter back to the original string — the same mechanism `TopicDetail`
already relies on for topic names that contain dots and other characters.

## Error handling

`GetUser` returns `null` for an unknown or not-yet-loaded principal; the page renders
the not-found state rather than throwing. No other failure modes are introduced (no
I/O, no new services). When the snapshot is not ready, `GetUser` already returns
`null` because the underlying graph is absent, so the not-found state covers the
pre-load case too.

## Testing

`UserDetailPageTests` in `Kafdoc.WebTest` (xUnit v3, bUnit, NSubstitute, snake_case
names, Arrange/Act/Assert), following `TopicsPageTests`:

- `UserDetail_renders_produce_and_consume_topic_links_for_a_known_principal` — stub
  `GetUser` to return a `UserDetailDto` with produce/consume topics; assert the markup
  contains the topic names and `/topics/...` hrefs.
- `UserDetail_renders_not_found_for_an_unknown_principal` — stub `GetUser` to return
  `null`; assert the "User not found" message renders.
- `UserDetail_renders_empty_note_when_user_has_no_produce_topics` — assert the empty
  produces note renders.

Add to a `UsersPageTests` class (new, mirroring `TopicsPageTests`):

- `Users_renders_a_link_to_the_user_detail_page_per_principal` — stub `GetUsers` and a
  ready `ISnapshotStatusService`; assert each principal renders as a `/users/...` link.

## Code style

XML doc comment on the new `Principal` parameter; `Uri.EscapeDataString` for link
building; markup and empty/not-found states copied from `TopicDetail.razor` for
visual consistency. No new packages. No registration changes (pages are discovered by
routing).
