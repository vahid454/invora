using Invora.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
namespace Invora.Api.Middleware;

public sealed class GlobalExceptionHandler(IProblemDetailsService problems, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var domain = exception as DomainException;
        var invalidInput = exception is BadHttpRequestException or System.Text.Json.JsonException;
        var status = domain?.Code switch
        {
            "INVALID_CREDENTIALS" or "INVALID_SESSION" => 401,
            "ACCESS_DENIED" or "OWNER_PROTECTED" => 403,
            "NOT_FOUND" => 404,
            "LICENSE_REQUIRED" => 402,
            "VALIDATION_FAILED" => 400,
            null => invalidInput ? (exception as BadHttpRequestException)?.StatusCode ?? 400 : 500,
            _ => 409
        };
        if (domain is null && !invalidInput) logger.LogError(exception, "Unhandled request failure {TraceId}", context.TraceIdentifier);
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new()
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = invalidInput ? "Check the entered details." : domain is null ? "An unexpected error occurred." : "Business operation rejected.",
                Detail = invalidInput ? "The request contains an invalid or missing value. Check product selection, dates and amounts, then try again." : domain?.Message,
                Extensions = { ["code"] = domain?.Code ?? (invalidInput ? "VALIDATION_FAILED" : "INTERNAL_ERROR"), ["traceId"] = context.TraceIdentifier }
            }
        });
    }
}
