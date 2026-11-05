namespace Claims.Api.Domain;

/// <summary>
/// Claim lifecycle. Allowed transitions are enforced by <see cref="Claim"/>:
/// Submitted -> UnderReview (officer picks up)
/// UnderReview -> InformationRequired | Approved | Rejected
/// InformationRequired -> UnderReview (claimant responds) | Rejected
/// Approved -> Settled
/// Rejected and Settled are terminal.
/// </summary>
public enum ClaimStatus
{
    Submitted,
    UnderReview,
    InformationRequired,
    Approved,
    Rejected,
    Settled
}

public enum ClaimType
{
    Motor,
    Property
}

public enum UserRole
{
    Claimant,
    Officer
}

/// <summary>Role names used in authorization attributes; must match <see cref="UserRole"/> names.</summary>
public static class Roles
{
    public const string Claimant = nameof(UserRole.Claimant);
    public const string Officer = nameof(UserRole.Officer);
}

/// <summary>Action identifiers returned to clients so the UI does not duplicate lifecycle rules.</summary>
public static class ClaimActions
{
    public const string Assign = "assign";
    public const string RequestInformation = "requestInformation";
    public const string ProvideInformation = "provideInformation";
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Settle = "settle";
}

/// <summary>The authenticated caller, as seen by the domain.</summary>
public record Actor(string Id, string Name, string Email, UserRole Role);
