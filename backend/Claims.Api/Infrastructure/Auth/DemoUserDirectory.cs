using Claims.Api.Domain;

namespace Claims.Api.Infrastructure.Auth;

public interface IUserDirectory
{
    IReadOnlyList<Actor> All { get; }
    Actor? Find(string id);
    IEnumerable<Actor> Officers => All.Where(u => u.Role == UserRole.Officer);
}

/// <summary>
/// Fixed demo identities. In production this is replaced by the identity provider (OIDC/OAuth2);
/// the rest of the system only depends on <see cref="Actor"/>.
/// </summary>
public class DemoUserDirectory : IUserDirectory
{
    public IReadOnlyList<Actor> All { get; } =
    [
        new("claimant-asha", "Asha Rao", "asha.rao@example.com", UserRole.Claimant),
        new("claimant-daniel", "Daniel Lim", "daniel.lim@example.com", UserRole.Claimant),
        new("claimant-mei", "Mei Tan", "mei.tan@example.com", UserRole.Claimant),
        new("officer-priya", "Priya Sharma", "priya.sharma@example.com", UserRole.Officer),
        new("officer-james", "James Wong", "james.wong@example.com", UserRole.Officer)
    ];

    public Actor? Find(string id) => All.FirstOrDefault(u => string.Equals(u.Id, id, StringComparison.OrdinalIgnoreCase));
}
