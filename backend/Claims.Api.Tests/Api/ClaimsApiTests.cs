using System.Net;
using System.Text.Json;
using Claims.Api.Contracts;
using Claims.Api.Domain;

namespace Claims.Api.Tests.Api;

public class ClaimsApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _asha;
    private readonly HttpClient _mei;
    private readonly HttpClient _priya;
    private readonly HttpClient _james;

    public ClaimsApiTests(ApiFactory factory)
    {
        _factory = factory;
        _asha = factory.ClientFor("claimant-asha");
        _mei = factory.ClientFor("claimant-mei");
        _priya = factory.ClientFor("officer-priya");
        _james = factory.ClientFor("officer-james");
    }

    private static object NewClaimBody(string market = "SG", decimal amount = 2000m) => new
    {
        policyNumber = "MTR-SG-0001",
        marketCode = market,
        claimType = "Motor",
        incidentDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)),
        description = "Rear-ended at traffic lights.",
        estimatedAmount = amount
    };

    private async Task<ClaimDetailDto> SubmitAsync(HttpClient client, string market = "SG", decimal amount = 2000m)
    {
        var response = await client.PostJsonAsync("/api/claims", NewClaimBody(market, amount));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<ClaimDetailDto>();
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    [Fact]
    public async Task Requests_without_identity_are_unauthorised()
    {
        var response = await _factory.CreateClient().GetAsync("/api/claims");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Submit_takes_claimant_identity_from_the_caller_not_the_body()
    {
        var claim = await SubmitAsync(_asha);

        Assert.Equal(ClaimStatus.Submitted, claim.Status);
        Assert.Equal("claimant-asha", claim.ClaimantId);
        Assert.Equal("Asha Rao", claim.ClaimantName);
        Assert.Equal("SGD", claim.Currency);
        Assert.StartsWith("CLM-", claim.ClaimNumber);
    }

    [Fact]
    public async Task Submit_with_missing_fields_returns_validation_problem()
    {
        var response = await _asha.PostJsonAsync("/api/claims", new { marketCode = "SG" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Submit_with_unknown_market_returns_400()
    {
        var response = await _asha.PostJsonAsync("/api/claims", NewClaimBody(market: "XX"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Officers_cannot_submit_and_claimants_cannot_use_officer_endpoints()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _priya.PostJsonAsync("/api/claims", NewClaimBody())).StatusCode);

        var claim = await SubmitAsync(_asha);
        Assert.Equal(HttpStatusCode.Forbidden, (await _asha.PostAsync($"/api/claims/{claim.Id}/assign", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _asha.GetAsync("/api/dashboard/workload")).StatusCode);
    }

    [Fact]
    public async Task Claimants_only_see_their_own_claims()
    {
        var ashas = await SubmitAsync(_asha);

        Assert.Equal(HttpStatusCode.NotFound, (await _mei.GetAsync($"/api/claims/{ashas.Id}")).StatusCode);
        var meiList = await (await _mei.GetAsync("/api/claims?pageSize=100")).ReadAsync<PagedResult<ClaimSummaryDto>>();
        Assert.DoesNotContain(meiList.Items, c => c.Id == ashas.Id);

        var officerView = await (await _priya.GetAsync($"/api/claims/{ashas.Id}")).ReadAsync<ClaimDetailDto>();
        Assert.Equal(ashas.Id, officerView.Id);
    }

    [Fact]
    public async Task Full_lifecycle_over_http_produces_history_and_async_notifications()
    {
        var claim = await SubmitAsync(_asha, amount: 3000m);

        claim = await (await _priya.PostAsync($"/api/claims/{claim.Id}/assign", null)).ReadAsync<ClaimDetailDto>();
        Assert.Equal(ClaimStatus.UnderReview, claim.Status);
        Assert.Equal("officer-priya", claim.AssignedOfficerId);

        claim = await (await _priya.PostJsonAsync($"/api/claims/{claim.Id}/information-requests",
            new { question = "Please upload photos of the damage." })).ReadAsync<ClaimDetailDto>();
        Assert.Equal(ClaimStatus.InformationRequired, claim.Status);
        var request = Assert.Single(claim.InformationRequests);

        // Claimant sees what they can do next, as computed by the server.
        var claimantView = await (await _asha.GetAsync($"/api/claims/{claim.Id}")).ReadAsync<ClaimDetailDto>();
        Assert.Equal(new[] { ClaimActions.ProvideInformation }, claimantView.AllowedActions);

        claim = await (await _asha.PostJsonAsync($"/api/claims/{claim.Id}/information-requests/{request.Id}/response",
            new { response = "Photos uploaded." })).ReadAsync<ClaimDetailDto>();
        Assert.Equal(ClaimStatus.UnderReview, claim.Status);

        claim = await (await _priya.PostJsonAsync($"/api/claims/{claim.Id}/approve",
            new { approvedAmount = 2800m, note = "Less excess" })).ReadAsync<ClaimDetailDto>();
        Assert.Equal(ClaimStatus.Approved, claim.Status);
        Assert.Equal(2800m, claim.OutstandingAmount);

        claim = await (await _priya.PostJsonAsync($"/api/claims/{claim.Id}/settle", new { })).ReadAsync<ClaimDetailDto>();
        Assert.Equal(ClaimStatus.Settled, claim.Status);
        Assert.Equal(2800m, claim.SettledAmount);
        Assert.Equal(6, claim.History.Count);

        // Notifications are produced asynchronously from outbox events, not by the request itself.
        await _factory.DispatchEventsAsync();
        var notifications = await (await _asha.GetAsync("/api/notifications")).ReadAsync<NotificationListDto>();
        var forClaim = notifications.Items.Where(n => n.ClaimId == claim.Id).Select(n => n.Title).ToList();
        Assert.Contains("Claim received", forClaim);
        Assert.Contains("More information needed", forClaim);
        Assert.Contains("Claim approved", forClaim);
        Assert.Contains("Claim settled", forClaim);

        var officerNotifications = await (await _priya.GetAsync("/api/notifications")).ReadAsync<NotificationListDto>();
        Assert.Contains(officerNotifications.Items, n => n.ClaimId == claim.Id && n.Title == "Claimant responded");
    }

    [Fact]
    public async Task Event_delivery_is_idempotent()
    {
        var claim = await SubmitAsync(_mei);
        await _factory.DispatchEventsAsync();
        await _factory.DispatchEventsAsync(); // nothing pending: no duplicates

        var notifications = await (await _mei.GetAsync("/api/notifications")).ReadAsync<NotificationListDto>();
        Assert.Single(notifications.Items, n => n.ClaimId == claim.Id);
    }

    [Fact]
    public async Task Invalid_transition_returns_409_with_code()
    {
        var claim = await SubmitAsync(_asha);
        await _priya.PostAsync($"/api/claims/{claim.Id}/assign", null);

        var response = await _priya.PostJsonAsync($"/api/claims/{claim.Id}/settle", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("invalid_state", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task A_claim_picked_up_by_one_officer_cannot_be_actioned_by_another()
    {
        var claim = await SubmitAsync(_asha);
        await _priya.PostAsync($"/api/claims/{claim.Id}/assign", null);

        Assert.Equal(HttpStatusCode.Conflict, (await _james.PostAsync($"/api/claims/{claim.Id}/assign", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _james.PostJsonAsync($"/api/claims/{claim.Id}/approve", new { approvedAmount = 10m })).StatusCode);
    }

    [Fact]
    public async Task Officer_queue_filters_unassigned_and_mine()
    {
        var unassigned = await SubmitAsync(_asha);
        var mine = await SubmitAsync(_asha);
        await _james.PostAsync($"/api/claims/{mine.Id}/assign", null);

        var unassignedList = await (await _james.GetAsync("/api/claims?assigned=unassigned&pageSize=100")).ReadAsync<PagedResult<ClaimSummaryDto>>();
        Assert.Contains(unassignedList.Items, c => c.Id == unassigned.Id);
        Assert.DoesNotContain(unassignedList.Items, c => c.Id == mine.Id);

        var myList = await (await _james.GetAsync("/api/claims?assigned=me&pageSize=100")).ReadAsync<PagedResult<ClaimSummaryDto>>();
        Assert.Contains(myList.Items, c => c.Id == mine.Id);
        Assert.All(myList.Items, c => Assert.Equal("officer-james", c.AssignedOfficerId));
    }
}
