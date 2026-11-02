using Claims.Api.Domain;

namespace Claims.Api.Tests.Domain;

public class ClaimLifecycleTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Actor Claimant = new("claimant-1", "Claire Claimant", "claire@example.com", UserRole.Claimant);
    private static readonly Actor OtherClaimant = new("claimant-2", "Oscar Other", "oscar@example.com", UserRole.Claimant);
    private static readonly Actor Officer = new("officer-1", "Olivia Officer", "olivia@example.com", UserRole.Officer);
    private static readonly Actor OtherOfficer = new("officer-2", "Oliver Officer", "oliver@example.com", UserRole.Officer);

    private static Claim NewClaim(decimal amount = 1000m) =>
        Claim.Submit("CLM-TEST-1", Claimant, "POL-1", Markets.Find("SG")!, ClaimType.Motor,
            new DateOnly(2026, 10, 30), "Collision in a car park.", amount, Now);

    private static Claim UnderReview()
    {
        var claim = NewClaim();
        claim.Assign(Officer, Now);
        return claim;
    }

    [Fact]
    public void Submit_sets_initial_state_identity_currency_and_raises_event()
    {
        var claim = NewClaim();

        Assert.Equal(ClaimStatus.Submitted, claim.Status);
        Assert.Equal(Claimant.Id, claim.ClaimantId);
        Assert.Equal(Claimant.Name, claim.ClaimantName);
        Assert.Equal("SGD", claim.Currency);
        Assert.Single(claim.History);
        Assert.IsType<ClaimSubmitted>(Assert.Single(claim.DomainEvents));
    }

    [Fact]
    public void Submit_rejects_future_incident_date_and_non_positive_amount()
    {
        Assert.Throws<DomainValidationException>(() => Claim.Submit("C", Claimant, "P", Markets.Find("SG")!, ClaimType.Motor,
            DateOnly.FromDateTime(Now.AddDays(1)), "Future incident", 100m, Now));
        Assert.Throws<DomainValidationException>(() => NewClaim(0m));
    }

    [Fact]
    public void Officers_cannot_submit_claims()
    {
        Assert.Throws<ClaimAccessException>(() => Claim.Submit("C", Officer, "P", Markets.Find("SG")!, ClaimType.Motor,
            new DateOnly(2026, 10, 1), "Description here", 100m, Now));
    }

    [Fact]
    public void Full_happy_path_reaches_settled_with_audit_trail_and_events()
    {
        var claim = NewClaim();
        claim.Assign(Officer, Now);
        var request = claim.RequestInformation(Officer, "Please send photos", Now);
        claim.ProvideInformation(Claimant, request.Id, "Photos attached", Now);
        claim.Approve(Officer, 900m, "Less excess", Now);
        claim.Settle(Officer, null, Now);

        Assert.Equal(ClaimStatus.Settled, claim.Status);
        Assert.Equal(900m, claim.SettledAmount);
        Assert.Equal(0m, claim.OutstandingLiability);
        Assert.Equal(6, claim.History.Count);
        Assert.Equal(new[] { typeof(ClaimSubmitted), typeof(ClaimAssigned), typeof(InformationRequested), typeof(InformationProvided), typeof(ClaimApproved), typeof(ClaimSettled) },
            claim.DomainEvents.Select(e => e.GetType()));
        Assert.Equal(6, claim.Version);
    }

    [Fact]
    public void Only_submitted_claims_can_be_picked_up()
    {
        var claim = UnderReview();
        Assert.Throws<ClaimStateException>(() => claim.Assign(OtherOfficer, Now));
    }

    [Fact]
    public void Only_the_assigned_officer_can_action_a_claim()
    {
        var claim = UnderReview();
        Assert.Throws<ClaimAccessException>(() => claim.Approve(OtherOfficer, 100m, null, Now));
        Assert.Throws<ClaimAccessException>(() => claim.RequestInformation(OtherOfficer, "Question?", Now));
    }

    [Fact]
    public void Unassigned_claim_cannot_be_decided()
    {
        var claim = NewClaim();
        Assert.Throws<ClaimAccessException>(() => claim.Approve(Officer, 100m, null, Now));
    }

    [Fact]
    public void Only_the_owning_claimant_can_respond_to_an_information_request()
    {
        var claim = UnderReview();
        var request = claim.RequestInformation(Officer, "Please send photos", Now);

        Assert.Throws<ClaimAccessException>(() => claim.ProvideInformation(OtherClaimant, request.Id, "Not mine", Now));
        Assert.Equal(ClaimStatus.InformationRequired, claim.Status);
    }

    [Fact]
    public void Cannot_approve_while_waiting_for_the_claimant()
    {
        var claim = UnderReview();
        claim.RequestInformation(Officer, "Please send photos", Now);
        Assert.Throws<ClaimStateException>(() => claim.Approve(Officer, 100m, null, Now));
    }

    [Fact]
    public void Rejecting_while_waiting_closes_the_open_information_request()
    {
        var claim = UnderReview();
        var request = claim.RequestInformation(Officer, "Please send photos", Now);

        claim.Reject(Officer, "No response from claimant", Now);

        Assert.Equal(ClaimStatus.Rejected, claim.Status);
        Assert.False(request.IsOpen);
        Assert.Equal(0m, claim.OutstandingLiability);
    }

    [Fact]
    public void Rejected_and_settled_claims_are_terminal()
    {
        var rejected = UnderReview();
        rejected.Reject(Officer, "Policy exclusion", Now);
        Assert.Throws<ClaimStateException>(() => rejected.Approve(Officer, 100m, null, Now));

        var settled = UnderReview();
        settled.Approve(Officer, 100m, null, Now);
        settled.Settle(Officer, null, Now);
        Assert.Throws<ClaimStateException>(() => settled.Reject(Officer, "Too late", Now));
    }

    [Fact]
    public void Settlement_cannot_exceed_approved_amount()
    {
        var claim = UnderReview();
        claim.Approve(Officer, 500m, null, Now);
        Assert.Throws<DomainValidationException>(() => claim.Settle(Officer, 501m, Now));
    }

    [Fact]
    public void Outstanding_liability_uses_estimate_until_approved_then_approved_amount()
    {
        var claim = UnderReview();
        Assert.Equal(1000m, claim.OutstandingLiability);

        claim.Approve(Officer, 750m, null, Now);
        Assert.Equal(750m, claim.OutstandingLiability);
    }

    [Fact]
    public void Allowed_actions_reflect_role_assignment_and_state()
    {
        var claim = NewClaim();
        Assert.Equal(new[] { ClaimActions.Assign }, claim.AllowedActions(Officer));
        Assert.Empty(claim.AllowedActions(Claimant));

        claim.Assign(Officer, Now);
        Assert.Equal(new[] { ClaimActions.RequestInformation, ClaimActions.Approve, ClaimActions.Reject }, claim.AllowedActions(Officer));
        Assert.Empty(claim.AllowedActions(OtherOfficer));

        claim.RequestInformation(Officer, "Photos please", Now);
        Assert.Equal(new[] { ClaimActions.ProvideInformation }, claim.AllowedActions(Claimant));
        Assert.Empty(claim.AllowedActions(OtherClaimant));
    }
}
