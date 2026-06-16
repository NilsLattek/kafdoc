using Kafdoc.Application.Dtos;
using Kafdoc.Application.Snapshot;
using Kafdoc.Domain.Documentation;
using Kafdoc.Domain.Graph;

namespace Kafdoc.Application.Services;

/// <summary>Computes user (principal) views from the current snapshot.</summary>
internal sealed class UserQueryService(ISnapshotStore store, IDocumentationStore documentation) : IUserQueryService
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
                consumes.GetValueOrDefault(u.Principal),
                documentation.HasDocumentation(DocumentationKind.User, u.Principal)))
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

        var doc = documentation.Read(DocumentationKind.User, principal);

        return new UserDetailDto(
            user.Principal,
            user.HasScramCredentials,
            DistinctSorted(graph.Producers.Where(p => Eq(p.Principal, principal)).Select(p => p.Topic)),
            DistinctSorted(graph.Consumers.Where(c => Eq(c.Principal, principal)).Select(c => c.Topic)),
            DistinctSorted(graph.UserGroups.Where(g => Eq(g.Principal, principal)).Select(g => g.GroupId)),
            doc.RelativePath,
            doc.Content);
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

    private static List<string> DistinctSorted(IEnumerable<string> source) =>
        source.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

    private static Dictionary<string, int> Counts(IEnumerable<(string Principal, string Topic)> edges) =>
        edges.GroupBy(e => e.Principal, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Topic).Distinct(StringComparer.Ordinal).Count(), StringComparer.Ordinal);
}
