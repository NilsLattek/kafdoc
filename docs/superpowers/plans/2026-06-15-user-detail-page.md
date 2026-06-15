# User Detail Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **No git actions:** The repository owner handles all staging/committing. Do **not** run `git add`, `git commit`, `git checkout`, or any other git command. Stop after each task's tests pass; the owner commits.

**Goal:** Make each principal on the Users page a link to a new `/users/{principal}` detail page that lists the topics the user produces to and consumes from, each linking onward to its topic detail page.

**Architecture:** Pure UI addition. The Application layer already exposes the data via `IUserQueryService.GetUser(principal)` returning `UserDetailDto(Principal, HasScramCredentials, ProducesTopics, ConsumesTopics, Groups)`. We add one new Blazor page (`UserDetail.razor`) mirroring the existing `TopicDetail.razor`, and turn the principal cell in `Users.razor` into a link — exactly as `Topics.razor` links topic names. No backend, DTO, domain, or DI changes.

**Tech Stack:** Blazor Server (interactive server render mode), .NET 10, xUnit v3 + bUnit + NSubstitute for component tests (`Kafdoc.WebTest`).

---

## File Structure

- **Create:** `src/Kafdoc.Web/Components/Pages/UserDetail.razor` — the `/users/{Principal}` detail page.
- **Modify:** `src/Kafdoc.Web/Components/Pages/Users.razor` — wrap the principal cell in a link to the detail page.
- **Create:** `test/Kafdoc.WebTest/UserDetailPageTests.cs` — bUnit tests for the new page.
- **Create:** `test/Kafdoc.WebTest/UsersPageTests.cs` — bUnit test that the list renders a detail link per principal.

Reference files to copy patterns from:
- `src/Kafdoc.Web/Components/Pages/TopicDetail.razor` (detail page shape, route parameter, not-found state, empty-state `<em>` notes, topic-link `href` form).
- `src/Kafdoc.Web/Components/Pages/Topics.razor` (the `Uri.EscapeDataString` link form in a table cell).
- `test/Kafdoc.WebTest/TopicsPageTests.cs` (bUnit test setup: `Bunit.BunitContext`, `Substitute.For<>`, `Services.AddSingleton`, `Render<T>`).

---

## Task 1: Users list links each principal to its detail page

**Files:**
- Test: `test/Kafdoc.WebTest/UsersPageTests.cs` (create)
- Modify: `src/Kafdoc.Web/Components/Pages/Users.razor` (the principal `<td>`)

- [ ] **Step 1: Write the failing test**

Create `test/Kafdoc.WebTest/UsersPageTests.cs`:

```csharp
using Bunit;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using Kafdoc.Application.Dtos;
using Kafdoc.Application.Services;
using Kafdoc.Web.Components.Pages;

namespace Kafdoc.WebTest;

public sealed class UsersPageTests : Bunit.BunitContext
{
    [Fact]
    public void Users_renders_a_link_to_the_user_detail_page_per_principal()
    {
        // Arrange
        var userQuery = Substitute.For<IUserQueryService>();
        userQuery.GetUsers().Returns(
        [
            new UserSummaryDto("User:alice", HasScramCredentials: true, ProducesCount: 2, ConsumesCount: 1),
        ]);
        var status = Substitute.For<ISnapshotStatusService>();
        status.GetStatus().Returns(new SnapshotStatusDto(IsReady: true, LastRefresh: DateTimeOffset.UnixEpoch, LastError: null));
        Services.AddSingleton(userQuery);
        Services.AddSingleton(status);

        // Act
        var cut = Render<Users>();

        // Assert
        Assert.Contains("href=\"/users/User%3Aalice\"", cut.Markup, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --no-restore test/Kafdoc.WebTest --filter-class "*UsersPageTests*"`
Expected: FAIL — the markup contains the plain principal text, not an `/users/...` link.

- [ ] **Step 3: Make the principal cell a link**

In `src/Kafdoc.Web/Components/Pages/Users.razor`, replace the principal cell:

```razor
                    <td>@u.Principal</td>
```

with:

```razor
                    <td><a href="@($"/users/{Uri.EscapeDataString(u.Principal)}")">@u.Principal</a></td>
```

Leave every other line of the page unchanged.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --no-restore test/Kafdoc.WebTest --filter-class "*UsersPageTests*"`
Expected: PASS.

- [ ] **Step 5: Stop for commit**

Do not run git. Report that Task 1 is complete and tests pass; the owner commits.

---

## Task 2: User detail page renders produce/consume topic links

**Files:**
- Test: `test/Kafdoc.WebTest/UserDetailPageTests.cs` (create)
- Create: `src/Kafdoc.Web/Components/Pages/UserDetail.razor`

- [ ] **Step 1: Write the failing tests**

Create `test/Kafdoc.WebTest/UserDetailPageTests.cs`:

```csharp
using Bunit;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using Kafdoc.Application.Dtos;
using Kafdoc.Application.Services;
using Kafdoc.Web.Components.Pages;

namespace Kafdoc.WebTest;

public sealed class UserDetailPageTests : Bunit.BunitContext
{
    [Fact]
    public void UserDetail_renders_produce_and_consume_topic_links_for_a_known_principal()
    {
        // Arrange
        var userQuery = Substitute.For<IUserQueryService>();
        userQuery.GetUser("User:alice").Returns(new UserDetailDto(
            Principal: "User:alice",
            HasScramCredentials: true,
            ProducesTopics: ["orders", "payments"],
            ConsumesTopics: ["shipments"],
            Groups: ["billing-svc"]));
        Services.AddSingleton(userQuery);

        // Act
        var cut = Render<UserDetail>(ps => ps.Add(p => p.Principal, "User:alice"));

        // Assert
        Assert.Contains("href=\"/topics/orders\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("href=\"/topics/payments\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("href=\"/topics/shipments\"", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void UserDetail_renders_not_found_for_an_unknown_principal()
    {
        // Arrange
        var userQuery = Substitute.For<IUserQueryService>();
        userQuery.GetUser(Arg.Any<string>()).Returns((UserDetailDto?)null);
        Services.AddSingleton(userQuery);

        // Act
        var cut = Render<UserDetail>(ps => ps.Add(p => p.Principal, "User:ghost"));

        // Assert
        Assert.Contains("User not found", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void UserDetail_renders_empty_note_when_user_has_no_produce_topics()
    {
        // Arrange
        var userQuery = Substitute.For<IUserQueryService>();
        userQuery.GetUser("User:reader").Returns(new UserDetailDto(
            Principal: "User:reader",
            HasScramCredentials: false,
            ProducesTopics: [],
            ConsumesTopics: ["shipments"],
            Groups: []));
        Services.AddSingleton(userQuery);

        // Act
        var cut = Render<UserDetail>(ps => ps.Add(p => p.Principal, "User:reader"));

        // Assert
        Assert.Contains("No producer ACLs", cut.Markup, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --no-restore test/Kafdoc.WebTest --filter-class "*UserDetailPageTests*"`
Expected: FAIL to compile — `UserDetail` does not exist yet.

- [ ] **Step 3: Create the detail page**

Create `src/Kafdoc.Web/Components/Pages/UserDetail.razor`:

```razor
@page "/users/{Principal}"
@rendermode InteractiveServer
@using Kafdoc.Application.Services
@inject IUserQueryService UserQuery

<PageTitle>User: @Principal</PageTitle>

<p><a href="/users">&larr; All users</a></p>

@{
    var detail = UserQuery.GetUser(Principal);
}

@if (detail is null)
{
    <h1>User not found</h1>
    <p>No principal named <code>@Principal</code> in the current snapshot.</p>
}
else
{
    <h1>@detail.Principal</h1>
    <p>SCRAM: @(detail.HasScramCredentials ? "yes" : "—")</p>

    <h2>Produces</h2>
    @if (detail.ProducesTopics.Count == 0)
    {
        <p><em>No producer ACLs.</em></p>
    }
    else
    {
        <ul>
            @foreach (var topic in detail.ProducesTopics)
            {
                <li><a href="@($"/topics/{Uri.EscapeDataString(topic)}")">@topic</a></li>
            }
        </ul>
    }

    <h2>Consumes</h2>
    @if (detail.ConsumesTopics.Count == 0)
    {
        <p><em>No consumer ACLs.</em></p>
    }
    else
    {
        <ul>
            @foreach (var topic in detail.ConsumesTopics)
            {
                <li><a href="@($"/topics/{Uri.EscapeDataString(topic)}")">@topic</a></li>
            }
        </ul>
    }
}

@code {
    /// <summary>The principal from the route.</summary>
    [Parameter]
    public string Principal { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --no-restore test/Kafdoc.WebTest --filter-class "*UserDetailPageTests*"`
Expected: PASS (all three tests).

- [ ] **Step 5: Stop for commit**

Do not run git. Report that Task 2 is complete and tests pass; the owner commits.

---

## Task 3: Full build and test sweep

**Files:** none (verification only)

- [ ] **Step 1: Build treating warnings as errors**

Run: `dotnet build --no-restore -warnaserror`
Expected: Build succeeds with no warnings (analyzers enforce style; the new `Principal` parameter has an XML doc comment to satisfy them).

- [ ] **Step 2: Run the full Web test project**

Run: `dotnet test --no-restore test/Kafdoc.WebTest`
Expected: PASS — `TopicsPageTests`, `UsersPageTests`, and `UserDetailPageTests` all green.

- [ ] **Step 3: Report**

Do not run git. Report final build/test output to the owner for review and committing.

---

## Self-Review Notes

- **Spec coverage:** Users-link (Task 1) ↔ spec "Users.razor edit"; UserDetail page with produce/consume topic links and not-found/empty states (Task 2) ↔ spec "UserDetail.razor"; no-backend-change and groups-not-shown honored (the page never references `detail.Groups`); routing-via-`Uri.EscapeDataString` covered in both tasks; testing section's four named tests all present.
- **Placeholder scan:** none — all steps contain full code and exact commands.
- **Type consistency:** `UserDetailDto` positional args (`Principal`, `HasScramCredentials`, `ProducesTopics`, `ConsumesTopics`, `Groups`) and `UserSummaryDto` args (`Principal`, `HasScramCredentials`, `ProducesCount`, `ConsumesCount`) match the existing DTO definitions; `IUserQueryService.GetUser`/`GetUsers` signatures match the interface; `ISnapshotStatusService.GetStatus()` and `SnapshotStatusDto(IsReady, LastRefresh, LastError)` match `TopicsPageTests` usage.
