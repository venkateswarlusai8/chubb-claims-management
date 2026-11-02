using Claims.Api.Contracts;

namespace Claims.Api.Tests.Api;

/// <summary>Uses its own factory (fresh database) so totals are exact.</summary>
public class DashboardApiTests : IDisposable
{
    private readonly ApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<ClaimDetailDto> SubmitAsync(HttpClient client, string market, decimal amount)
    {
        var response = await client.PostJsonAsync("/api/claims", new
        {
            policyNumber = "POL-123",
            marketCode = market,
            claimType = "Property",
            incidentDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            description = "Water damage from burst pipe.",
            estimatedAmount = amount
        });
        return await response.ReadAsync<ClaimDetailDto>();
    }

    [Fact]
    public async Task Exposure_counts_estimates_for_undecided_and_approved_amounts_for_approved_claims()
    {
        var claimant = _factory.ClientFor("claimant-daniel");
        var officer = _factory.ClientFor("officer-priya");

        await SubmitAsync(claimant, "SG", 1000m);                       // undecided: estimate counts
        var approved = await SubmitAsync(claimant, "SG", 5000m);        // approved at 4000: approved amount counts
        var rejected = await SubmitAsync(claimant, "AU", 9000m);        // rejected: excluded

        await officer.PostAsync($"/api/claims/{approved.Id}/assign", null);
        await officer.PostJsonAsync($"/api/claims/{approved.Id}/approve", new { approvedAmount = 4000m });
        await officer.PostAsync($"/api/claims/{rejected.Id}/assign", null);
        await officer.PostJsonAsync($"/api/claims/{rejected.Id}/reject", new { reason = "Not covered by policy" });

        var exposure = await (await officer.GetAsync("/api/dashboard/exposure")).ReadAsync<ExposureDto>();

        var sg = Assert.Single(exposure.ByMarket, m => m.MarketCode == "SG");
        Assert.Equal(2, sg.OpenClaims);
        Assert.Equal(1000m, sg.EstimatedOutstanding);
        Assert.Equal(4000m, sg.ApprovedAwaitingSettlement);
        Assert.Equal(5000m, sg.TotalOutstanding);
        Assert.Equal(3700m, sg.TotalOutstandingReporting); // 5000 SGD x 0.74 (appsettings rate)

        Assert.Equal(0, Assert.Single(exposure.ByMarket, m => m.MarketCode == "AU").OpenClaims);
        Assert.Equal(2, exposure.OpenClaims);
        Assert.Equal(3700m, exposure.TotalOutstanding);
    }

    [Fact]
    public async Task Workload_reports_unassigned_queue_and_per_officer_counts()
    {
        var claimant = _factory.ClientFor("claimant-asha");
        var priya = _factory.ClientFor("officer-priya");

        await SubmitAsync(claimant, "HK", 100m);
        var picked = await SubmitAsync(claimant, "HK", 200m);
        await priya.PostAsync($"/api/claims/{picked.Id}/assign", null);

        var workload = await (await priya.GetAsync("/api/dashboard/workload")).ReadAsync<WorkloadDto>();

        Assert.Equal(2, workload.OpenClaims);
        Assert.Equal(1, workload.UnassignedClaims);
        Assert.Equal(1, workload.MyOpenClaims);
        var priyaRow = Assert.Single(workload.Officers, o => o.OfficerId == "officer-priya");
        Assert.Equal(1, priyaRow.UnderReview);
        Assert.Single(workload.OldestUnassigned);
    }
}
