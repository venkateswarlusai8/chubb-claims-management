using Claims.Api.Contracts;
using Claims.Api.Domain;
using Claims.Api.Infrastructure.Auth;
using Claims.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Claims.Api.Controllers;

/// <summary>Workload and exposure views for claims staff.</summary>
[ApiController]
[Route("api/dashboard")]
[Authorize(Roles = Roles.Officer)]
[Produces("application/json")]
public class DashboardController(DashboardService dashboard) : ControllerBase
{
    //[HttpGet("workload")]
    //public Task<WorkloadDto> Workload(CancellationToken ct) => dashboard.GetWorkloadAsync(User.ToActor(), ct);

    //[HttpGet("exposure")]
    //public Task<ExposureDto> Exposure(CancellationToken ct) => dashboard.GetExposureAsync(ct);


    [HttpGet("workload")]
    public Task<WorkloadDto> Workload(CancellationToken ct)
    {
        return dashboard.GetWorkloadAsync(User.ToActor(), ct);
    }

    [HttpGet("exposure")]
    public Task<ExposureDto> Exposure(CancellationToken ct)
    {
        return dashboard.GetExposureAsync(ct);
    }
}

[ApiController]
[Route("api/notifications")]
[Authorize]
[Produces("application/json")]
public class NotificationsController(NotificationService notifications) : ControllerBase
{
    //[HttpGet]
    //public Task<NotificationListDto> List(CancellationToken ct) => notifications.ListAsync(User.ToActor(), ct);

    //[HttpPost("{id:guid}/read")]
    //public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    //{
    //    await notifications.MarkReadAsync(User.ToActor(), id, ct);
    //    return NoContent();
    //}

    //[HttpPost("read-all")]
    //public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    //{
    //    await notifications.MarkAllReadAsync(User.ToActor(), ct);
    //    return NoContent();
    //}



    [HttpGet]
    public Task<NotificationListDto> List(CancellationToken ct)
    {
        return notifications.ListAsync(User.ToActor(), ct);
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(User.ToActor(), id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(User.ToActor(), ct);
        return NoContent();
    }

}

[ApiController]
[Route("api")]
[Produces("application/json")]
public class IdentityController(IUserDirectory users) : ControllerBase
{
    ///// <summary>The caller's identity as resolved by the server.</summary>
    //[HttpGet("me")]
    //[Authorize]
    //public UserDto Me()
    //{
    //    var actor = User.ToActor();
    //    return new UserDto(actor.Id, actor.Name, actor.Email, actor.Role);
    //}

    ///// <summary>Demo-only: identities the UI can switch between. Would not exist with a real identity provider.</summary>
    //[HttpGet("demo-users")]
    //[AllowAnonymous]
    ////public IEnumerable<UserDto> DemoUsers() => users.All.Select(u => new UserDto(u.Id, u.Name, u.Email, u.Role));
    //public IEnumerable<UserDto> DemoUsers()
    //{
    //    return users.All.Select(u => new UserDto(u.Id, u.Name, u.Email, u.Role));
    //}

    //[HttpGet("reference-data")]
    //[AllowAnonymous]
    //public ReferenceDataDto ReferenceData() =>
    //    new(Markets.All, Enum.GetValues<ClaimType>(), Enum.GetValues<ClaimStatus>());



    /// <summary>The caller's identity as resolved by the server.</summary>
    [HttpGet("me")]
    [Authorize]
    public UserDto Me()
    {
        var actor = User.ToActor();
        return new UserDto(actor.Id, actor.Name, actor.Email, actor.Role);
    }

    /// <summary>Demo-only: identities the UI can switch between. Would not exist with a real identity provider.</summary>
    [HttpGet("demo-users")]
    [AllowAnonymous]
    public IEnumerable<UserDto> DemoUsers()
    {
        return users.All.Select(u => new UserDto(u.Id, u.Name, u.Email, u.Role));
    }

    [HttpGet("reference-data")]
    [AllowAnonymous]
    public ReferenceDataDto ReferenceData()
    {
        return new ReferenceDataDto(Markets.All, Enum.GetValues<ClaimType>(), Enum.GetValues<ClaimStatus>());
    }



}
