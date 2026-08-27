using System.ComponentModel.DataAnnotations;

namespace Cadence.Api.Requests;

/// <summary>
/// Request bodies. They are deliberately separate from the application-layer
/// handler arguments: model binding has to cope with absent and malformed input,
/// and what a handler receives should already be past that.
/// </summary>
/// <remarks>
/// Attributes here target the <em>constructor parameter</em>, not the generated
/// property. MVC throws at bind time - not compile time - if it finds validation
/// metadata on a property that came from a record's primary constructor, because
/// it validates the parameter and the attribute would be silently ignored. So
/// <c>[Required]</c>, never <c>[property: Required]</c>.
/// </remarks>
public sealed record RegisterRequest(
    [Required][EmailAddress][MaxLength(256)] string Email,
    [Required][MaxLength(120)] string DisplayName,
    [Required][MinLength(8)][MaxLength(256)] string Password);

public sealed record LoginRequest(
    [Required][EmailAddress][MaxLength(256)] string Email,
    [Required][MaxLength(256)] string Password);

/// <summary>Body of <c>POST /api/v1/coaching/reports</c>; every field is optional.</summary>
/// <param name="Weeks">Size of the training window to assess.</param>
/// <param name="Refresh">
/// Forces a new model call even when a report already covers this window. Off by
/// default: generating one costs money, and a page refresh must not spend it.
/// </param>
public sealed record GenerateCoachingReportRequest(
    [Range(1, 52)] int? Weeks,
    bool? Refresh);

/// <summary>Body of <c>PATCH /api/v1/auth/me</c>. Every field is optional; omitting one clears it.</summary>
public sealed record UpdateProfileRequest(
    [Range(80, 260)] int? MaxHeartRate,
    [Range(25, 150)] int? RestingHeartRate,
    [Range(1900, 2100)] int? BirthYear,
    [Range(20, 400)] double? WeightKilograms);
