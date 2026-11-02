using System.Security.Cryptography;

namespace Claims.Api.Domain;

/// <summary>
/// Claim aggregate root. All lifecycle changes go through its methods so the state machine,
/// ownership rules, audit history and domain events stay consistent in one place.
/// </summary>
public class Claim
{
    private readonly List<IClaimEvent> _domainEvents = [];

    private Claim() { } // EF Core

    public Guid Id { get; private set; }
    public string ClaimNumber { get; private set; } = "";

    public string ClaimantId { get; private set; } = "";
    public string ClaimantName { get; private set; } = "";
    public string ClaimantEmail { get; private set; } = "";

    public string PolicyNumber { get; private set; } = "";
    public string MarketCode { get; private set; } = "";
    public string Currency { get; private set; } = "";
    public ClaimType Type { get; private set; }
    public DateOnly IncidentDate { get; private set; }
    public string Description { get; private set; } = "";

    /// <summary>Claimant's estimate of the loss; used as the indicative reserve until a decision.</summary>
    public decimal EstimatedAmount { get; private set; }
    public decimal? ApprovedAmount { get; private set; }
    public decimal? SettledAmount { get; private set; }
    public string? DecisionReason { get; private set; }

    public ClaimStatus Status { get; private set; }
    public string? AssignedOfficerId { get; private set; }
    public string? AssignedOfficerName { get; private set; }

    public DateTime SubmittedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? AssignedAt { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public DateTime? SettledAt { get; private set; }

    /// <summary>Optimistic concurrency token; incremented on every change.</summary>
    public int Version { get; private set; }

    public List<InformationRequest> InformationRequests { get; private set; } = [];
    public List<ClaimHistoryEntry> History { get; private set; } = [];

    public IReadOnlyList<IClaimEvent> DomainEvents => _domainEvents;
    public void ClearDomainEvents() => _domainEvents.Clear();

    public bool IsOpen => Status is not (ClaimStatus.Settled or ClaimStatus.Rejected);

    public InformationRequest? OpenInformationRequest => InformationRequests.FirstOrDefault(r => r.IsOpen);

    /// <summary>
    /// Prototype liability rule: approved-but-unpaid claims carry the approved amount; undecided open
    /// claims carry the claimant's estimate; settled and rejected claims carry nothing.
    /// </summary>
    public decimal OutstandingLiability => Status switch
    {
        ClaimStatus.Approved => ApprovedAmount ?? 0m,
        ClaimStatus.Settled or ClaimStatus.Rejected => 0m,
        _ => EstimatedAmount
    };

    public static string GenerateClaimNumber(DateTime now) =>
        $"CLM-{now:yyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(3))}";

    public static Claim Submit(
        string claimNumber, Actor claimant, string policyNumber, MarketInfo market, ClaimType type,
        DateOnly incidentDate, string description, decimal estimatedAmount, DateTime now)
    {
        RequireRole(claimant, UserRole.Claimant);
        if (estimatedAmount <= 0)
            throw new DomainValidationException("Estimated amount must be greater than zero.");
        if (incidentDate > DateOnly.FromDateTime(now))
            throw new DomainValidationException("Incident date cannot be in the future.");
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainValidationException("A description of the incident is required.");

        var claim = new Claim
        {
            Id = Guid.NewGuid(),
            ClaimNumber = claimNumber,
            ClaimantId = claimant.Id,
            ClaimantName = claimant.Name,
            ClaimantEmail = claimant.Email,
            PolicyNumber = policyNumber.Trim(),
            MarketCode = market.Code,
            Currency = market.Currency,
            Type = type,
            IncidentDate = incidentDate,
            Description = description.Trim(),
            EstimatedAmount = estimatedAmount,
            Status = ClaimStatus.Submitted,
            SubmittedAt = now,
            UpdatedAt = now,
            Version = 1
        };
        claim.Record(claimant, "Submitted", null, ClaimStatus.Submitted, null, now);
        claim.Raise(new ClaimSubmitted(claim.Id, claim.ClaimNumber, now, claim.ClaimantId,
            claim.MarketCode, claim.Type, claim.EstimatedAmount, claim.Currency));
        return claim;
    }

    /// <summary>An officer picks up an unassigned submitted claim; it moves to UnderReview.</summary>
    public void Assign(Actor officer, DateTime now)
    {
        RequireRole(officer, UserRole.Officer);
        if (Status != ClaimStatus.Submitted)
            throw new ClaimStateException($"Only submitted claims can be picked up. This claim is {Status}.");

        AssignedOfficerId = officer.Id;
        AssignedOfficerName = officer.Name;
        AssignedAt = now;
        Transition(officer, "PickedUp", ClaimStatus.UnderReview, $"Assigned to {officer.Name}", now);
        Raise(new ClaimAssigned(Id, ClaimNumber, now, ClaimantId, officer.Id, officer.Name));
    }

    public InformationRequest RequestInformation(Actor officer, string question, DateTime now)
    {
        RequireAssignedOfficer(officer);
        if (string.IsNullOrWhiteSpace(question))
            throw new DomainValidationException("A question for the claimant is required.");
        if (Status != ClaimStatus.UnderReview)
            throw new ClaimStateException($"Information can only be requested while the claim is under review. This claim is {Status}.");

        var request = InformationRequest.Create(Id, officer, question.Trim(), now);
        InformationRequests.Add(request);
        Transition(officer, "InformationRequested", ClaimStatus.InformationRequired, request.Question, now);
        Raise(new InformationRequested(Id, ClaimNumber, now, ClaimantId, request.Id, request.Question));
        return request;
    }

    public void ProvideInformation(Actor claimant, Guid requestId, string response, DateTime now)
    {
        RequireOwner(claimant);
        if (string.IsNullOrWhiteSpace(response))
            throw new DomainValidationException("A response is required.");
        if (Status != ClaimStatus.InformationRequired)
            throw new ClaimStateException($"No information is currently required for this claim. It is {Status}.");

        var request = InformationRequests.FirstOrDefault(r => r.Id == requestId)
            ?? throw new NotFoundException("Information request not found on this claim.");
        if (!request.IsOpen)
            throw new ClaimStateException("This information request has already been answered or closed.");

        request.Answer(response.Trim(), now);
        Transition(claimant, "InformationProvided", ClaimStatus.UnderReview, null, now);
        Raise(new InformationProvided(Id, ClaimNumber, now, AssignedOfficerId, request.Id));
    }

    public void Approve(Actor officer, decimal approvedAmount, string? note, DateTime now)
    {
        RequireAssignedOfficer(officer);
        if (Status != ClaimStatus.UnderReview)
            throw new ClaimStateException($"Only claims under review can be approved. This claim is {Status}.");
        if (approvedAmount <= 0)
            throw new DomainValidationException("Approved amount must be greater than zero.");

        ApprovedAmount = approvedAmount;
        DecisionReason = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        DecidedAt = now;
        Transition(officer, "Approved", ClaimStatus.Approved, DecisionReason, now);
        Raise(new ClaimApproved(Id, ClaimNumber, now, ClaimantId, approvedAmount, Currency));
    }

    public void Reject(Actor officer, string reason, DateTime now)
    {
        RequireAssignedOfficer(officer);
        if (Status is not (ClaimStatus.UnderReview or ClaimStatus.InformationRequired))
            throw new ClaimStateException($"Only claims under review or awaiting information can be rejected. This claim is {Status}.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainValidationException("A reason is required to reject a claim.");

        OpenInformationRequest?.Close(now);
        DecisionReason = reason.Trim();
        DecidedAt = now;
        Transition(officer, "Rejected", ClaimStatus.Rejected, DecisionReason, now);
        Raise(new ClaimRejected(Id, ClaimNumber, now, ClaimantId, DecisionReason));
    }

    public void Settle(Actor officer, decimal? settledAmount, DateTime now)
    {
        RequireAssignedOfficer(officer);
        if (Status != ClaimStatus.Approved)
            throw new ClaimStateException($"Only approved claims can be settled. This claim is {Status}.");

        var amount = settledAmount ?? ApprovedAmount!.Value;
        if (amount <= 0 || amount > ApprovedAmount)
            throw new DomainValidationException($"Settlement amount must be greater than zero and no more than the approved amount ({ApprovedAmount:N2}).");

        SettledAmount = amount;
        SettledAt = now;
        Transition(officer, "Settled", ClaimStatus.Settled, $"Paid {Currency} {amount:N2}", now);
        Raise(new ClaimSettled(Id, ClaimNumber, now, ClaimantId, amount, Currency));
    }

    public bool CanView(Actor actor) => actor.Role == UserRole.Officer || actor.Id == ClaimantId;

    /// <summary>Actions the given user may take now. Lets the UI render buttons without duplicating rules.</summary>
    public IReadOnlyList<string> AllowedActions(Actor actor)
    {
        var actions = new List<string>();
        if (actor.Role == UserRole.Officer)
        {
            if (Status == ClaimStatus.Submitted) actions.Add(ClaimActions.Assign);
            if (AssignedOfficerId == actor.Id)
            {
                switch (Status)
                {
                    case ClaimStatus.UnderReview:
                        actions.AddRange([ClaimActions.RequestInformation, ClaimActions.Approve, ClaimActions.Reject]);
                        break;
                    case ClaimStatus.InformationRequired:
                        actions.Add(ClaimActions.Reject);
                        break;
                    case ClaimStatus.Approved:
                        actions.Add(ClaimActions.Settle);
                        break;
                }
            }
        }
        else if (actor.Id == ClaimantId && Status == ClaimStatus.InformationRequired && OpenInformationRequest is not null)
        {
            actions.Add(ClaimActions.ProvideInformation);
        }
        return actions;
    }

    private void Transition(Actor actor, string action, ClaimStatus to, string? note, DateTime now)
    {
        var from = Status;
        Status = to;
        UpdatedAt = now;
        Version++;
        Record(actor, action, from, to, note, now);
    }

    private void Record(Actor actor, string action, ClaimStatus? from, ClaimStatus to, string? note, DateTime now) =>
        History.Add(ClaimHistoryEntry.Create(Id, actor, action, from, to, note, now));

    private void Raise(IClaimEvent evt) => _domainEvents.Add(evt);

    private static void RequireRole(Actor actor, UserRole role)
    {
        if (actor.Role != role)
            throw new ClaimAccessException($"This action requires the {role} role.");
    }

    private void RequireOwner(Actor actor)
    {
        RequireRole(actor, UserRole.Claimant);
        if (actor.Id != ClaimantId)
            throw new ClaimAccessException("Only the claimant who submitted this claim can do this.");
    }

    private void RequireAssignedOfficer(Actor actor)
    {
        RequireRole(actor, UserRole.Officer);
        if (AssignedOfficerId != actor.Id)
            throw new ClaimAccessException(AssignedOfficerId is null
                ? "Pick up the claim before actioning it."
                : $"Only the assigned officer ({AssignedOfficerName}) can action this claim.");
    }
}
