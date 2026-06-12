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
