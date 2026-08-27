using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Api.Infrastructure;

/// <summary>
/// Turns an unhandled fault into an RFC 7807 body. Expected failures never reach
/// here - they travel as <c>Result</c> values - so anything caught in this
/// middleware is genuinely unexpected and is logged at error level.
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    private static readonly JsonSerializerOptions ProblemJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client hung up mid-request. There is nobody left to read a body,
            // and this is not an error worth paging anyone about.
            logger.LogDebug(
                "Request {Method} {Path} was aborted by the client.",
                context.Request.Method,
                context.Request.Path);
        }
        catch (MissingAthleteClaimException ex)
        {
            logger.LogWarning(ex, "Authenticated request reached {Path} without a usable subject claim.", context.Request.Path);
            await WriteProblemAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                "The access token does not identify an athlete.");
        }
        catch (BadHttpRequestException ex)
        {
            // Kestrel raises this for oversized or malformed bodies and already
            // carries the status code it wants (413 for a request that ran past
            // the size limit, 400 otherwise).
            logger.LogInformation(
                "Rejected {Method} {Path}: {Reason}",
                context.Request.Method,
                context.Request.Path,
                ex.Message);
            await WriteProblemAsync(context, ex.StatusCode, "Bad request", ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unhandled exception while handling {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);

            // Deliberately generic: the exception text can carry connection
            // strings and file paths, and this response is public.
            await WriteProblemAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "Unexpected error",
                "An unexpected error occurred while processing the request.");
        }
    }

    private async Task WriteProblemAsync(HttpContext context, int statusCode, string title, string detail)
    {
        if (context.Response.HasStarted)
        {
            // Headers are already on the wire; overwriting them would corrupt the
            // response, so the only honest option is to let the truncated body go.
            logger.LogWarning(
                "The response for {Path} had already started, so the failure could not be reported as ProblemDetails.",
                context.Request.Path);
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path,
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, ProblemJsonOptions));
    }
}
