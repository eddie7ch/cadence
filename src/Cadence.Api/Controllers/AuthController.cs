using Cadence.Api.Infrastructure;
using Cadence.Api.Requests;
using Cadence.Application.Handlers;
using Cadence.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(
    RegisterAthleteHandler registerAthleteHandler,
    AuthenticateAthleteHandler authenticateAthleteHandler,
    GetAthleteHandler getAthleteHandler,
    ICurrentAthlete currentAthlete) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponseDto>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await registerAthleteHandler.ExecuteAsync(
            request.Email,
            request.DisplayName,
            request.Password,
            cancellationToken);

        return result.ToActionResult(this, response => Created("/api/v1/auth/me", response));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponseDto>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await authenticateAthleteHandler.ExecuteAsync(
            request.Email,
            request.Password,
            cancellationToken);

        return result.ToActionResult(this);
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<AthleteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AthleteDto>> Me(CancellationToken cancellationToken)
    {
        var result = await getAthleteHandler.ExecuteAsync(currentAthlete.Id, cancellationToken);
        return result.ToActionResult(this);
    }
}
