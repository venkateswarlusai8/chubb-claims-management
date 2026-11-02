using System.ComponentModel.DataAnnotations;
using Claims.Api.Domain;

namespace Claims.Api.Contracts;

// ---- Requests --------------------------------------------------------------------------------
// Claimant identity (name, email) comes from the authenticated user, never from the request body.

public sealed record SubmitClaimRequest(
    [Required, StringLength(40, MinimumLength = 3)] string PolicyNumber,
    [Required, StringLength(4)] string MarketCode,
    [Required] ClaimType? ClaimType,
    [Required] DateOnly? IncidentDate,
    [Required, StringLength(2000, MinimumLength = 10)] string Description,
    [Required, Range(0.01, 100_000_000)] decimal? EstimatedAmount);

public sealed record RequestInformationRequest(
    [Required, StringLength(1000, MinimumLength = 5)] string Question);

public sealed record ProvideInformationRequest(
    [Required, StringLength(2000, MinimumLength = 2)] string Response);

public sealed record ApproveClaimRequest(
    [Required, Range(0.01, 100_000_000)] decimal? ApprovedAmount,
    [StringLength(1000)] string? Note);

public sealed record RejectClaimRequest(
    [Required, StringLength(1000, MinimumLength = 5)] string Reason);

public sealed record SettleClaimRequest(
    [Range(0.01, 100_000_000)] decimal? SettledAmount);

public sealed class ClaimListQuery
{
    public ClaimStatus? Status { get; set; }
    /// <summary>true = only open claims (not Settled/Rejected).</summary>
    public bool? Open { get; set; }
    /// <summary>"me" = assigned to the calling officer, "unassigned" = no officer yet.</summary>
    public string? Assigned { get; set; }
    public string? Market { get; set; }
    /// <summary>Matches claim number, claimant name or policy number.</summary>
    public string? Search { get; set; }
    /// <summary>"newest" (default) or "oldest" (queue order).</summary>
    public string? Sort { get; set; }
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}

// ---- Responses -------------------------------------------------------------------------------

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record ClaimSummaryDto(
    Guid Id, string ClaimNumber, string ClaimantName, string MarketCode, string MarketName,
    ClaimType ClaimType, ClaimStatus Status, decimal EstimatedAmount, decimal OutstandingAmount, string Currency,
    string? AssignedOfficerId, string? AssignedOfficerName, DateTime SubmittedAt, DateTime UpdatedAt,
    int AgeDays, bool AwaitingClaimant);

public sealed record ClaimDetailDto(
    Guid Id, string ClaimNumber, ClaimStatus Status,
    string ClaimantId, string ClaimantName, string ClaimantEmail,
    string PolicyNumber, string MarketCode, string MarketName, string Currency, ClaimType ClaimType,
    DateOnly IncidentDate, string Description,
    decimal EstimatedAmount, decimal? ApprovedAmount, decimal? SettledAmount, decimal OutstandingAmount,
    string? DecisionReason, string? AssignedOfficerId, string? AssignedOfficerName,
    DateTime SubmittedAt, DateTime UpdatedAt, DateTime? DecidedAt, DateTime? SettledAt,
    int AgeDays, int Version,
    IReadOnlyList<InformationRequestDto> InformationRequests,
    IReadOnlyList<ClaimHistoryDto> History,
    IReadOnlyList<string> AllowedActions);

public sealed record InformationRequestDto(
    Guid Id, string Question, string RequestedByName, DateTime RequestedAt,
    string? Response, DateTime? RespondedAt, bool IsOpen);

public sealed record ClaimHistoryDto(
    DateTime OccurredAt, string Action, ClaimStatus? FromStatus, ClaimStatus ToStatus,
    string ActorName, UserRole ActorRole, string? Note);

public sealed record WorkloadDto(
    DateTime GeneratedAt, int TotalClaims, int OpenClaims, int UnassignedClaims, int MyOpenClaims,
    int SlaDays, int SlaBreaches,
    IReadOnlyList<StatusCountDto> ByStatus,
    IReadOnlyList<AgingBucketDto> Aging,
    IReadOnlyList<OfficerWorkloadDto> Officers,
    IReadOnlyList<ClaimSummaryDto> OldestUnassigned);

public sealed record StatusCountDto(ClaimStatus Status, int Count);

public sealed record AgingBucketDto(string Label, int Count);

public sealed record OfficerWorkloadDto(
    string OfficerId, string OfficerName, int OpenAssigned, int UnderReview, int AwaitingClaimant,
    int AwaitingSettlement, int DecidedLast30Days, int SettledLast30Days, double? AverageDaysToDecision);

public sealed record ExposureDto(
    DateTime GeneratedAt, string ReportingCurrency, int OpenClaims,
    decimal TotalOutstanding, decimal EstimatedOutstanding, decimal ApprovedAwaitingSettlement, decimal SettledLast30Days,
    IReadOnlyList<MarketExposureDto> ByMarket,
    IReadOnlyList<ClaimTypeExposureDto> ByClaimType,
    IReadOnlyList<ClaimSummaryDto> LargestOpenClaims,
    string Basis);

public sealed record MarketExposureDto(
    string MarketCode, string MarketName, string Currency, int OpenClaims,
    decimal EstimatedOutstanding, decimal ApprovedAwaitingSettlement, decimal TotalOutstanding,
    decimal TotalOutstandingReporting);

public sealed record ClaimTypeExposureDto(ClaimType ClaimType, int OpenClaims, decimal TotalOutstandingReporting);

public sealed record NotificationDto(
    Guid Id, Guid ClaimId, string ClaimNumber, string Title, string Message, DateTime CreatedAt, bool IsRead);

public sealed record NotificationListDto(IReadOnlyList<NotificationDto> Items, int UnreadCount);

public sealed record UserDto(string Id, string Name, string Email, UserRole Role);

public sealed record ReferenceDataDto(
    IReadOnlyList<MarketInfo> Markets, IReadOnlyList<ClaimType> ClaimTypes, IReadOnlyList<ClaimStatus> Statuses);
