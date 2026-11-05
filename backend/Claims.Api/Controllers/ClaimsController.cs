using Claims.Api.Contracts;
using Claims.Api.Domain;
using Claims.Api.Infrastructure.Auth;
using Claims.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Claims.Api.Controllers;

/// <summary>
/// Claims resource. Claimants see and act on their own claims; officers see all claims and act on
/// the ones assigned to them. Lifecycle changes are explicit command endpoints rather than a generic
/// "PATCH status", so each transition has its own validation and audit entry.
/// </summary>
[ApiController]
[Route("api/claims")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public class ClaimsController(ClaimService claims) : ControllerBase
{
    /// <summary>List claims visible to the caller (claimant: own claims; officer: all), with filters and paging.</summary>
    [HttpGet]
    public Task<PagedResult<ClaimSummaryDto>> List([FromQuery] ClaimListQuery query, CancellationToken ct) =>
        claims.ListAsync(User.ToActor(), query, ct);

    /// <summary>Claim detail including history, information requests and the actions the caller may take.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ClaimDetailDto> Get(Guid id, CancellationToken ct) =>
        claims.GetAsync(User.ToActor(), id, ct);

    /// <summary>Claimant reports an incident.</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Claimant)]
    [ProducesResponseType<ClaimDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ClaimDetailDto>> Submit(SubmitClaimRequest request, CancellationToken ct)
    {
        var claim = await claims.SubmitAsync(User.ToActor(), request, ct);
        return CreatedAtAction(nameof(Get), new { id = claim.Id }, claim);
    }

    /// <summary>Officer picks up a submitted claim (assigns it to themselves; status becomes UnderReview).</summary>
    [HttpPost("{id:guid}/assign")]
    [Authorize(Roles = Roles.Officer)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ClaimDetailDto> Assign(Guid id, CancellationToken ct) =>
        claims.AssignAsync(User.ToActor(), id, ct);

    /// <summary>Assigned officer asks the claimant for more information.</summary>
    [HttpPost("{id:guid}/information-requests")]
    [Authorize(Roles = Roles.Officer)]
    public Task<ClaimDetailDto> RequestInformation(Guid id, RequestInformationRequest request, CancellationToken ct) =>
        claims.RequestInformationAsync(User.ToActor(), id, request, ct);

    /// <summary>Claimant answers an open information request; the claim returns to UnderReview.</summary>
    [HttpPost("{id:guid}/information-requests/{requestId:guid}/response")]
    [Authorize(Roles = Roles.Claimant)]
    public Task<ClaimDetailDto> ProvideInformation(Guid id, Guid requestId, ProvideInformationRequest request, CancellationToken ct) =>
        claims.ProvideInformationAsync(User.ToActor(), id, requestId, request, ct);

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = Roles.Officer)]
    public Task<ClaimDetailDto> Approve(Guid id, ApproveClaimRequest request, CancellationToken ct) =>
        claims.ApproveAsync(User.ToActor(), id, request, ct);

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = Roles.Officer)]
    public Task<ClaimDetailDto> Reject(Guid id, RejectClaimRequest request, CancellationToken ct) =>
        claims.RejectAsync(User.ToActor(), id, request, ct);

    /// <summary>Record payment of an approved claim. Amount defaults to the approved amount.</summary>
    [HttpPost("{id:guid}/settle")]
    [Authorize(Roles = Roles.Officer)]
    public Task<ClaimDetailDto> Settle(Guid id, SettleClaimRequest request, CancellationToken ct) =>
        claims.SettleAsync(User.ToActor(), id, request, ct);
}
