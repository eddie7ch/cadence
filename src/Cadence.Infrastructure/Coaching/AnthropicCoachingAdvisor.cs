using System.Globalization;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Cadence.Application.Abstractions;
using Cadence.Domain.Coaching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cadence.Infrastructure.Coaching;

public sealed class AnthropicCoachingAdvisor : ICoachingAdvisor
{
    private const string SystemPrompt =
        """
        You are an endurance coach reviewing one athlete's recent training block.

        You are given weekly rollups and per-activity summaries that have already
        been aggregated; you never see raw sensor samples. Reason only from the
        numbers supplied. Pace values are seconds per kilometre, so a smaller
        number is faster, and grade-adjusted pace is the flat-equivalent of the
        same effort. Never invent a figure that is not in the data, and never
        offer medical advice.

        Judge the block against this athlete's own recent history rather than
        against any population norm:
          - detraining   - load is falling and fitness is being lost
          - maintaining  - load is flat and adequate to hold current fitness
          - productive   - load is rising at a rate the athlete is absorbing
          - overreaching - load is rising faster than the athlete is absorbing it

        Each finding cites the one metric that drove it. Use severity "info" for
        an observation, "warning" for something to correct this week, and
        "critical" only for a trajectory that risks injury or illness.
        """;

    private static readonly JsonSerializerOptions ResponseJson = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions PromptJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private static readonly IReadOnlyDictionary<string, JsonElement> ResponseSchema = BuildResponseSchema();

    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicCoachingAdvisor> _logger;
    private readonly AnthropicClient? _client;

    public AnthropicCoachingAdvisor(IOptions<AnthropicOptions> options, ILogger<AnthropicCoachingAdvisor> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        _client = _options.IsConfigured ? new AnthropicClient { ApiKey = _options.ApiKey! } : null;
    }

    public bool IsConfigured => _client is not null;

    public async Task<CoachingAnalysis> AnalyzeAsync(
        CoachingInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (_client is null)
        {
            throw new InvalidOperationException(
                "Anthropic:ApiKey is not configured; check ICoachingAdvisor.IsConfigured before calling AnalyzeAsync.");
        }

        var parameters = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            System = SystemPrompt,
            Messages = [new MessageParam { Role = Role.User, Content = BuildUserPrompt(input) }],
            OutputConfig = new OutputConfig
            {
                Format = new JsonOutputFormat { Schema = ResponseSchema },
            },
        };

        var message = await _client.Messages.Create(parameters, cancellationToken).ConfigureAwait(false);
        var payload = ExtractText(message);

        CoachingResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<CoachingResponse>(payload, ResponseJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"The coaching model returned content that is not valid JSON: {Excerpt(payload)}",
                ex);
        }

        if (response is null)
        {
            throw new InvalidOperationException("The coaching model returned a JSON null instead of an analysis.");
        }

        return MapAnalysis(response);
    }

    private CoachingAnalysis MapAnalysis(CoachingResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Summary))
        {
            throw new InvalidOperationException("The coaching model omitted the required 'summary' field.");
        }

        var verdict = ParseVerdict(response.Verdict);

        if (response.Findings is not { Count: > 0 })
        {
            throw new InvalidOperationException("The coaching model returned no findings.");
        }

        var findings = new List<CoachingFinding>(response.Findings.Count);
        foreach (var finding in response.Findings)
        {
            if (finding is null
                || string.IsNullOrWhiteSpace(finding.Title)
                || string.IsNullOrWhiteSpace(finding.Detail)
                || string.IsNullOrWhiteSpace(finding.Metric)
                || string.IsNullOrWhiteSpace(finding.Severity))
            {
                throw new InvalidOperationException(
                    "The coaching model returned a finding missing one of title, detail, metric or severity.");
            }

            findings.Add(new CoachingFinding(
                finding.Title.Trim(),
                finding.Detail.Trim(),
                finding.Metric.Trim(),
                finding.Severity.Trim().ToLowerInvariant()));
        }

        _logger.LogDebug(
            "Coaching analysis produced verdict {Verdict} with {FindingCount} findings.",
            verdict,
            findings.Count);

        return new CoachingAnalysis(response.Summary.Trim(), verdict, findings, _options.Model);
    }

    private static TrainingLoadVerdict ParseVerdict(string? verdict) => verdict?.Trim().ToLowerInvariant() switch
    {
        "detraining" => TrainingLoadVerdict.Detraining,
        "maintaining" => TrainingLoadVerdict.Maintaining,
        "productive" => TrainingLoadVerdict.Productive,
        "overreaching" => TrainingLoadVerdict.Overreaching,

        // Unknown is the domain's "we never asked", not a landing place for a
        // model that ignored the schema. Half-parsing here would store a verdict
        // nobody can account for.
        _ => throw new InvalidOperationException(
            $"The coaching model returned an unrecognised verdict '{verdict}'."),
    };

    private static string ExtractText(Message message)
    {
        var builder = new StringBuilder();

        foreach (var block in message.Content)
        {
            if (block.TryPickText(out var text) && text is not null)
            {
                builder.Append(text.Text);
            }
        }

        if (builder.Length == 0)
        {
            throw new InvalidOperationException(
                "The coaching model returned no text content, so there is no analysis to parse.");
        }

        return builder.ToString();
    }

    private static string BuildUserPrompt(CoachingInput input)
    {
        var payload = new
        {
            periodStart = input.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            periodEnd = input.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            maxHeartRate = input.MaxHeartRate,
            weeks = input.Weeks.Select(week => new
            {
                weekStart = week.WeekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                activities = week.ActivityCount,
                distanceKm = Math.Round(week.DistanceMeters / 1000d, 2),
                elevationGainM = Math.Round(week.ElevationGainMeters, 0),
                movingMinutes = Math.Round(week.MovingSeconds / 60d, 1),
                averageHeartRateBpm = week.AverageHeartRateBpm is { } bpm ? Math.Round(bpm, 0) : (double?)null,
            }).ToList(),
            recentActivities = input.RecentActivities.Select(activity => new
            {
                date = activity.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                sport = activity.Sport,
                distanceKm = Math.Round(activity.DistanceKm, 2),
                movingMinutes = Math.Round(activity.MovingMinutes, 1),
                paceSecPerKm = Math.Round(activity.PaceSecondsPerKm, 0),
                gradeAdjustedPaceSecPerKm = Math.Round(activity.GradeAdjustedPaceSecondsPerKm, 0),
                elevationGainM = Math.Round(activity.ElevationGainMeters, 0),
                averageHeartRateBpm = activity.AverageHeartRateBpm,
            }).ToList(),
        };

        return $"""
            Assess this training block and return the analysis as JSON.

            {JsonSerializer.Serialize(payload, PromptJson)}
            """;
    }

    private static IReadOnlyDictionary<string, JsonElement> BuildResponseSchema() =>
        new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(new
            {
                summary = new
                {
                    type = "string",
                    description = "Two to four sentences on how the block went and what to do next.",
                },
                verdict = new
                {
                    type = "string",
                    @enum = new[] { "detraining", "maintaining", "productive", "overreaching" },
                },
                findings = new
                {
                    type = "array",
                    minItems = 1,
                    maxItems = 6,
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            title = new { type = "string", description = "Short label, at most eight words." },
                            detail = new { type = "string", description = "One or two sentences citing the numbers." },
                            metric = new
                            {
                                type = "string",
                                description = "The single metric this finding rests on, e.g. 'weekly distance'.",
                            },
                            severity = new
                            {
                                type = "string",
                                @enum = new[] { "info", "warning", "critical" },
                            },
                        },
                        required = new[] { "title", "detail", "metric", "severity" },
                        additionalProperties = false,
                    },
                },
            }),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "summary", "verdict", "findings" }),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        };

    private static string Excerpt(string payload) =>
        payload.Length <= 400 ? payload : string.Concat(payload.AsSpan(0, 400), "...");

    private sealed record CoachingResponse(
        string? Summary,
        string? Verdict,
        IReadOnlyList<CoachingResponseFinding?>? Findings);

    private sealed record CoachingResponseFinding(
        string? Title,
        string? Detail,
        string? Metric,
        string? Severity);
}
