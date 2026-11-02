using Claims.Api.Contracts;
using Claims.Api.Domain;
using Claims.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Services;

/// <summary>
/// Application service for the Claims module: loads the aggregate, invokes the domain method,
/// saves (which also writes outbox events) and maps to DTOs. Business rules live in <see cref="Claim"/>.
/// </summary>
public sealed class ClaimService(ClaimsDbContext db, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<ClaimDetailDto> SubmitAsync(Actor claimant, SubmitClaimRequest request, CancellationToken ct)
    {
        var market = Markets.Find(request.MarketCode)
            ?? throw new DomainValidationException($"Unknown market '{request.MarketCode}'.");
        var now = Now;
        var claim = Claim.Submit(Claim.GenerateClaimNumber(now), claimant, request.PolicyNumber, market,
            request.ClaimType!.Value, request.IncidentDate!.Value, request.Description, request.EstimatedAmount!.Value, now);

        db.Claims.Add(claim);
        await db.SaveChangesAsync(ct);
        return ClaimMapper.ToDetail(claim, claimant, now);
    }

    public async Task<PagedResult<ClaimSummaryDto>> ListAsync(Actor user, ClaimListQuery q, CancellationToken ct)
    {
        IQueryable<Claim> query = db.Claims.AsNoTracking();

        if (user.Role == UserRole.Claimant)
            query = query.Where(c => c.ClaimantId == user.Id);
        if (q.Status is { } status)
            query = query.Where(c => c.Status == status);
        if (q.Open == true)
            query = query.Where(c => c.Status != ClaimStatus.Settled && c.Status != ClaimStatus.Rejected);
        if (!string.IsNullOrWhiteSpace(q.Market))
            query = query.Where(c => c.MarketCode == q.Market.ToUpper());

        switch (q.Assigned?.Trim().ToLowerInvariant())
        {
            case "me": query = query.Where(c => c.AssignedOfficerId == user.Id); break;
            case "unassigned": query = query.Where(c => c.AssignedOfficerId == null); break;
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim();
            query = query.Where(c => c.ClaimNumber.Contains(term) || c.ClaimantName.Contains(term) || c.PolicyNumber.Contains(term));
        }

        var total = await query.CountAsync(ct);
        query = q.Sort?.ToLowerInvariant() == "oldest"
            ? query.OrderBy(c => c.SubmittedAt)
            : query.OrderByDescending(c => c.SubmittedAt);

        var rows = await query
            .Skip((q.Page - 1) * q.PageSize)
            .Take(q.PageSize)
            .Select(c => new { Claim = c, AwaitingClaimant = c.InformationRequests.Any(r => r.Response == null && r.ClosedAt == null) })
            .ToListAsync(ct);

        var now = Now;
        return new PagedResult<ClaimSummaryDto>(
            rows.Select(r => ClaimMapper.ToSummary(r.Claim, r.AwaitingClaimant, now)).ToList(), q.Page, q.PageSize, total);
    }

    public async Task<ClaimDetailDto> GetAsync(Actor user, Guid id, CancellationToken ct)
    {
        var claim = await LoadAsync(user, id, tracking: false, ct);
        return ClaimMapper.ToDetail(claim, user, Now);
    }

    public Task<ClaimDetailDto> AssignAsync(Actor officer, Guid id, CancellationToken ct) =>
        ExecuteAsync(officer, id, (c, now) => c.Assign(officer, now), ct);

    public Task<ClaimDetailDto> RequestInformationAsync(Actor officer, Guid id, RequestInformationRequest r, CancellationToken ct) =>
        ExecuteAsync(officer, id, (c, now) => c.RequestInformation(officer, r.Question, now), ct);

    public Task<ClaimDetailDto> ProvideInformationAsync(Actor claimant, Guid id, Guid requestId, ProvideInformationRequest r, CancellationToken ct) =>
        ExecuteAsync(claimant, id, (c, now) => c.ProvideInformation(claimant, requestId, r.Response, now), ct);

    public Task<ClaimDetailDto> ApproveAsync(Actor officer, Guid id, ApproveClaimRequest r, CancellationToken ct) =>
        ExecuteAsync(officer, id, (c, now) => c.Approve(officer, r.ApprovedAmount!.Value, r.Note, now), ct);

    public Task<ClaimDetailDto> RejectAsync(Actor officer, Guid id, RejectClaimRequest r, CancellationToken ct) =>
        ExecuteAsync(officer, id, (c, now) => c.Reject(officer, r.Reason, now), ct);

    public Task<ClaimDetailDto> SettleAsync(Actor officer, Guid id, SettleClaimRequest r, CancellationToken ct) =>
        ExecuteAsync(officer, id, (c, now) => c.Settle(officer, r.SettledAmount, now), ct);

    private async Task<ClaimDetailDto> ExecuteAsync(Actor user, Guid id, Action<Claim, DateTime> action, CancellationToken ct)
    {
        var claim = await LoadAsync(user, id, tracking: true, ct);
        var now = Now;
        action(claim, now);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ClaimStateException("This claim was changed by someone else. Refresh and try again.", "concurrency_conflict");
        }
        return ClaimMapper.ToDetail(claim, user, now);
    }

    private async Task<Claim> LoadAsync(Actor user, Guid id, bool tracking, CancellationToken ct)
    {
        var query = db.Claims.Include(c => c.InformationRequests).Include(c => c.History).AsSplitQuery();
        var claim = await (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(c => c.Id == id, ct);

        // A claimant asking for someone else's claim gets 404, not 403, so claim ids cannot be probed.
        if (claim is null || !claim.CanView(user))
            throw new NotFoundException($"Claim {id} was not found.");
        return claim;
    }
}

public static class ClaimMapper
{
    public static int AgeDays(Claim c, DateTime now)
    {
        var end = c.IsOpen ? now : c.SettledAt ?? c.DecidedAt ?? c.UpdatedAt;
        return Math.Max(0, (int)(end - c.SubmittedAt).TotalDays);
    }

    public static ClaimSummaryDto ToSummary(Claim c, bool awaitingClaimant, DateTime now) => new(
        c.Id, c.ClaimNumber, c.ClaimantName, c.MarketCode, Markets.NameOf(c.MarketCode),
        c.Type, c.Status, c.EstimatedAmount, c.OutstandingLiability, c.Currency,
        c.AssignedOfficerId, c.AssignedOfficerName, c.SubmittedAt, c.UpdatedAt,
        AgeDays(c, now), awaitingClaimant);

    public static ClaimDetailDto ToDetail(Claim c, Actor viewer, DateTime now) => new(
        c.Id, c.ClaimNumber, c.Status,
        c.ClaimantId, c.ClaimantName, c.ClaimantEmail,
        c.PolicyNumber, c.MarketCode, Markets.NameOf(c.MarketCode), c.Currency, c.Type,
        c.IncidentDate, c.Description,
        c.EstimatedAmount, c.ApprovedAmount, c.SettledAmount, c.OutstandingLiability,
        c.DecisionReason, c.AssignedOfficerId, c.AssignedOfficerName,
        c.SubmittedAt, c.UpdatedAt, c.DecidedAt, c.SettledAt,
        AgeDays(c, now), c.Version,
        c.InformationRequests.OrderBy(r => r.RequestedAt)
            .Select(r => new InformationRequestDto(r.Id, r.Question, r.RequestedByName, r.RequestedAt, r.Response, r.RespondedAt, r.IsOpen))
            .ToList(),
        c.History.OrderBy(h => h.OccurredAt)
            .Select(h => new ClaimHistoryDto(h.OccurredAt, h.Action, h.FromStatus, h.ToStatus, h.ActorName, h.ActorRole, h.Note))
            .ToList(),
        c.AllowedActions(viewer));
}
