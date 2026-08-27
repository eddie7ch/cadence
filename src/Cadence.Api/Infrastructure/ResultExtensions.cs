using Cadence.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Api.Infrastructure;

/// <summary>
/// The single place where an application-layer <see cref="Error"/> becomes an HTTP
/// status code. Keeping the mapping here means no controller has to remember that
/// "not found" is 404 and no layer below the API has to know HTTP exists at all.
/// </summary>
public static class ResultExtensions
{
    public static ActionResult<T> ToActionResult<T>(this Result<T> result, ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        return result.IsSuccess
            ? controller.Ok(result.Value)
            : controller.ToProblem(result.Error!);
    }

    /// <summary>Overload for endpoints whose success status is not 200 - 201, 202, and so on.</summary>
    public static ActionResult<T> ToActionResult<T>(
        this Result<T> result,
        ControllerBase controller,
        Func<T, ActionResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.IsSuccess
            ? onSuccess(result.Value!)
            : controller.ToProblem(result.Error!);
    }

    public static ActionResult ToActionResult(this Result result, ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        return result.IsSuccess
            ? controller.NoContent()
            : controller.ToProblem(result.Error!);
    }

    public static ObjectResult ToProblem(this ControllerBase controller, Error error)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(error);

        return controller.Problem(
            detail: error.Message,
            statusCode: ToStatusCode(error.Kind),
            title: ToTitle(error.Kind));
    }

    public static int ToStatusCode(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.Unprocessable => StatusCodes.Status422UnprocessableEntity,
        ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,

        // ErrorKind.None on a failed Result is a bug in the handler, not a client
        // mistake, so it must not be reported as one.
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string ToTitle(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => "Invalid request",
        ErrorKind.NotFound => "Not found",
        ErrorKind.Conflict => "Conflict",
        ErrorKind.Unauthorized => "Unauthorized",
        ErrorKind.Forbidden => "Forbidden",
        ErrorKind.Unprocessable => "Unprocessable entity",
        ErrorKind.Unavailable => "Service unavailable",
        _ => "Unexpected error",
    };
}
