using System.ComponentModel.DataAnnotations;

namespace Cadence.Api.Requests;

/// <summary>
/// Request bodies. They are deliberately separate from the application-layer
/// commands: model binding has to cope with absent and malformed input, and a
/// command that a handler receives should already be past that.
/// </summary>
public sealed record RegisterRequest(
    [property: Required]
    [property: EmailAddress]
    [property: MaxLength(256)]
    string Email,
    [property: Required]
    [property: MaxLength(120)]
    string DisplayName,
    [property: Required]
    [property: MinLength(8)]
    [property: MaxLength(256)]
    string Password);

public sealed record LoginRequest(
    [property: Required]
    [property: EmailAddress]
    [property: MaxLength(256)]
    string Email,
    [property: Required]
    [property: MaxLength(256)]
    string Password);

/// <summary>Body of <c>POST /api/v1/coaching/reports</c>; every field is optional.</summary>
public sealed record GenerateCoachingReportRequest(
    [property: Range(1, 52)]
    int? Weeks,
    /// <summary>
    /// Forces a new model call even when a report already covers this window.
    /// Off by default: generating costs money, and a page refresh must not spend it.
    /// </summary>
    bool? Refresh);
