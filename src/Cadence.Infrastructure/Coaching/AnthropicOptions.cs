namespace Cadence.Infrastructure.Coaching;

public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public const string DefaultModel = "claude-opus-5";

    public const int DefaultMaxTokens = 4096;

    /// <summary>
    /// Null, empty or whitespace means the advisor is switched off. Compose
    /// substitutes an unset variable as an empty string, and an empty key that
    /// counted as "configured" would produce a feature that advertises itself and
    /// then fails on every call.
    /// </summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = DefaultModel;

    public int MaxTokens { get; set; } = DefaultMaxTokens;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
