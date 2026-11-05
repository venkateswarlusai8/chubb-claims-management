using Claims.Api.Contracts;
using Claims.Api.Domain;
using Claims.Api.Infrastructure.Auth;
using Claims.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Claims.Api.Services;

public class DashboardOptions
{
    /// <summary>Open claims older than this many days count as SLA breaches.</summary>
    public int SlaDays { get; set; } = 14;
}

public class FxOptions
{
    public string ReportingCurrency { get; set; } = "USD";
    /// <summary>Indicative rate: 1 unit of the currency = N units of the reporting currency.</summary>
    public Dictionary<string, decimal> Rates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Reporting module: workload and liability exposure for claims staff.
/// Prototype computes in memory from the claims table. At scale this becomes a read model
/// (projected from claim events) or SQL aggregation; the API contract would not change.
/// </summary>
public class DashboardService(
    ClaimsDbContext db,
    IUserDirectory users,
    TimeProvider clock,
    IOptions<DashboardOptions> dashboardOptions,
    IOptions<FxOptions> fxOptions)
{
    public async Task<WorkloadDto> GetWorkloadAsync(Actor officer, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var slaDays = dashboardOptions.Value.SlaDays;
        var claims = await db.Claims.AsNoTracking().ToListAsync(ct);
        var open = claims.Where(c => c.IsOpen).ToList();
        var since30 = now.AddDays(-30);

        var byStatus = Enum.GetValues<ClaimStatus>()
            .Select(s => new StatusCountDto(s, claims.Count(c => c.Status == s)))
            .ToList();

        int Age(Claim c) => ClaimMapper.AgeDays(c, now);
        var aging = new List<AgingBucketDto>
        {
            new("0-7 days", open.Count(c => Age(c) <= 7)),
            new("8-14 days", open.Count(c => Age(c) is > 7 and <= 14)),
            new("15-30 days", open.Count(c => Age(c) is > 14 and <= 30)),
            new("30+ days", open.Count(c => Age(c) > 30))
        };

        var officers = users.Officers
            .Select(o =>
            {
                var mine = claims.Where(c => c.AssignedOfficerId == o.Id).ToList();
                var decided = mine.Where(c => c.DecidedAt is not null).ToList();
                return new OfficerWorkloadDto(
                    o.Id, o.Name,
                    OpenAssigned: mine.Count(c => c.IsOpen),
                    UnderReview: mine.Count(c => c.Status == ClaimStatus.UnderReview),
                    AwaitingClaimant: mine.Count(c => c.Status == ClaimStatus.InformationRequired),
                    AwaitingSettlement: mine.Count(c => c.Status == ClaimStatus.Approved),
                    DecidedLast30Days: decided.Count(c => c.DecidedAt >= since30),
                    SettledLast30Days: mine.Count(c => c.SettledAt >= since30),
                    AverageDaysToDecision: decided.Count == 0
                        ? null
                        : Math.Round(decided.Average(c => (c.DecidedAt!.Value - c.SubmittedAt).TotalDays), 1));
            })
            .ToList();

        var oldestUnassigned = open
            .Where(c => c.AssignedOfficerId is null)
            .OrderBy(c => c.SubmittedAt)
            .Take(5)
            .Select(c => ClaimMapper.ToSummary(c, false, now))
            .ToList();

        return new WorkloadDto(
            now, claims.Count, open.Count,
            UnassignedClaims: open.Count(c => c.AssignedOfficerId is null),
            MyOpenClaims: open.Count(c => c.AssignedOfficerId == officer.Id),
            slaDays,
            SlaBreaches: open.Count(c => Age(c) > slaDays),
            byStatus, aging, officers, oldestUnassigned);
    }

    public async Task<ExposureDto> GetExposureAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var fx = fxOptions.Value;
        var claims = await db.Claims.AsNoTracking().ToListAsync(ct);
        var open = claims.Where(c => c.IsOpen).ToList();

        decimal ToReporting(decimal amount, string currency) =>
            fx.Rates.TryGetValue(currency, out var rate)
                ? Math.Round(amount * rate, 2)
                : throw new InvalidOperationException($"No FX rate configured for {currency}.");

        var byMarket = Markets.All
            .Select(m =>
            {
                var inMarket = open.Where(c => c.MarketCode == m.Code).ToList();
                var estimated = inMarket.Where(c => c.Status != ClaimStatus.Approved).Sum(c => c.OutstandingLiability);
                var approved = inMarket.Where(c => c.Status == ClaimStatus.Approved).Sum(c => c.OutstandingLiability);
                return new MarketExposureDto(m.Code, m.Name, m.Currency, inMarket.Count,
                    estimated, approved, estimated + approved, ToReporting(estimated + approved, m.Currency));
            })
            .ToList();

        var byType = Enum.GetValues<ClaimType>()
            .Select(t =>
            {
                var ofType = open.Where(c => c.Type == t).ToList();
                return new ClaimTypeExposureDto(t, ofType.Count, ofType.Sum(c => ToReporting(c.OutstandingLiability, c.Currency)));
            })
            .ToList();

        var largest = open
            .OrderByDescending(c => ToReporting(c.OutstandingLiability, c.Currency))
            .Take(5)
            .Select(c => ClaimMapper.ToSummary(c, false, now))
            .ToList();

        var estimatedReporting = open.Where(c => c.Status != ClaimStatus.Approved).Sum(c => ToReporting(c.OutstandingLiability, c.Currency));
        var approvedReporting = open.Where(c => c.Status == ClaimStatus.Approved).Sum(c => ToReporting(c.OutstandingLiability, c.Currency));
        var settled30 = claims.Where(c => c.SettledAt >= now.AddDays(-30)).Sum(c => ToReporting(c.SettledAmount ?? 0, c.Currency));

        return new ExposureDto(
            now, fx.ReportingCurrency, open.Count,
            estimatedReporting + approvedReporting, estimatedReporting, approvedReporting, settled30,
            byMarket, byType, largest,
            Basis: "Outstanding = approved amount for approved, unpaid claims + claimant estimate for undecided open claims. " +
                   "Converted at static indicative FX rates; excludes reserves adjustments, recoveries, excess and policy limits.");
    }
}
