using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Rezerv.Domain.Common;
using StackExchange.Redis;

namespace Rezerv.Api.Infrastructure;

/// <summary>Turns exceptions into RFC 7807 problem details with a stable "code" field.</summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problemDetails)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, title, detail) = exception switch
        {
            DomainException de => (MapStatus(de.Kind), de.Code, TitleFor(de.Kind), de.Message),
            RedisConnectionException or RedisTimeoutException =>
                (StatusCodes.Status503ServiceUnavailable, "LOCK_SERVICE_UNAVAILABLE", "Service unavailable",
                 "Booking is temporarily unavailable. Please try again shortly."),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested =>
                (499, "REQUEST_CANCELLED", "Request cancelled", "The client closed the request."),
            _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "Unexpected error",
                  "Something went wrong on our side.")
        };

        if (status >= 500)
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        else
            logger.LogInformation("Request rejected with {Code}: {Message}", code, exception.Message);

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
                Extensions = { ["code"] = code }
            }
        });
    }

    private static int MapStatus(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status422UnprocessableEntity
    };

    private static string TitleFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => "Invalid request",
        ErrorKind.NotFound => "Not found",
        ErrorKind.Conflict => "Conflict",
        _ => "Business rule violated"
    };
}
