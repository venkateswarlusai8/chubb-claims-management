using Claims.Api.Domain;
using Microsoft.AspNetCore.Diagnostics;

namespace Claims.Api.Infrastructure.Web;

/// <summary>Maps domain exceptions to RFC 7807 problem responses with a machine-readable <c>code</c>.</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title, code) = exception switch
        {
            DomainValidationException => (StatusCodes.Status400BadRequest, "Validation failed", "validation_failed"),
            ClaimAccessException => (StatusCodes.Status403Forbidden, "Forbidden", "forbidden"),
            NotFoundException => (StatusCodes.Status404NotFound, "Not found", "not_found"),
            ClaimStateException state => (StatusCodes.Status409Conflict, "Conflict", state.Code),
            _ => (0, "", "")
        };

        if (status == 0)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            return false; // default handler returns a generic 500 problem without internals
        }

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails =
            {
                Status = status,
                Title = title,
                Detail = exception.Message,
                Extensions = { ["code"] = code }
            }
        });
    }
}
