using System.Buffers;
using System.Security.Cryptography;
using Cadence.Application.Abstractions;
using Cadence.Application.Common;
using Cadence.Application.Contracts;
using Cadence.Application.Mapping;
using Cadence.Domain.Activities;

namespace Cadence.Application.Handlers;

/// <summary>
/// Records an uploaded device file as a pending activity and stores its bytes.
/// Parsing into metrics is <see cref="ProcessActivityHandler"/>'s job, and runs
/// off the request thread.
/// </summary>
public sealed class ImportActivityHandler
{
    /// <summary>
    /// Enough of the file for a parser to recognise a magic number without
    /// reading a 20 MB FIT file twice.
    /// </summary>
    private const int HeaderBytes = 512;

    /// <summary>
    /// The whole upload is buffered to compute its checksum and to hand the
    /// parser a rewindable stream, so an unbounded upload would be an unbounded
    /// allocation. No consumer device produces an activity file near this size.
    /// </summary>
    private const long MaximumFileBytes = 64L * 1024 * 1024;

    private readonly IActivityRepository _activities;
    private readonly IActivityFileParserFactory _parserFactory;
    private readonly IActivityFileStore _files;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ImportActivityHandler(
        IActivityRepository activities,
        IActivityFileParserFactory parserFactory,
        IActivityFileStore files,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentNullException.ThrowIfNull(parserFactory);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(clock);

        _activities = activities;
        _parserFactory = parserFactory;
        _files = files;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<ActivitySummaryDto>> ExecuteAsync(
        Guid athleteId,
        string? fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Error.Validation("A file name is required.");
        }

        // A browser may send a full client-side path; only the leaf is ours to keep.
        string safeFileName = Path.GetFileName(fileName.Trim());
        if (safeFileName.Length == 0)
        {
            return Error.Validation("A file name is required.");
        }

        byte[]? bytes = await ReadBoundedAsync(content, cancellationToken);
        if (bytes is null)
        {
            return Error.Validation($"The file exceeds the {MaximumFileBytes / (1024 * 1024)} MB upload limit.");
        }

        if (bytes.Length == 0)
        {
            return Error.Validation("The uploaded file is empty.");
        }

        string checksum = Convert.ToHexStringLower(SHA256.HashData(bytes));

        // Re-uploading the same file is a no-op, not a conflict: watch software
        // syncs the same directory repeatedly and the caller wants the activity
        // it already has, not an error it has to special-case.
        Activity? existing = await _activities.FindByChecksumAsync(athleteId, checksum, cancellationToken);
        if (existing is not null)
        {
            return existing.ToSummaryDto();
        }

        IActivityFileParser parser;
        try
        {
            parser = _parserFactory.Resolve(safeFileName, bytes.AsSpan(0, Math.Min(HeaderBytes, bytes.Length)));
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return Error.Unprocessable($"'{safeFileName}' is not a recognised activity file: {ex.Message}");
        }

        // The sport and the recorded name live on the activity from the moment it
        // exists and there is no later opportunity to set them, so the file is
        // decoded here as well as during processing.
        ParsedActivity parsed;
        try
        {
            using var buffer = new MemoryStream(bytes, writable: false);
            parsed = await parser.ParseAsync(buffer, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error.Unprocessable($"'{safeFileName}' could not be decoded: {ex.Message}");
        }

        Activity activity = Activity.Import(
            athleteId,
            ResolveName(parsed.Name, safeFileName),
            parsed.Sport,
            parsed.Format is SourceFormat.Unknown ? parser.Format : parsed.Format,
            safeFileName,
            checksum,
            _clock.UtcNow);

        // Bytes first, row second. A stored file with no row is unreferenced
        // clutter; a row pointing at a file that was never written is an activity
        // that can never be processed.
        await _files.SaveAsync(athleteId, checksum, safeFileName, bytes, cancellationToken);

        _activities.Add(activity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return activity.ToSummaryDto();
    }

    private static string ResolveName(string? parsedName, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(parsedName))
        {
            return parsedName.Trim();
        }

        string withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(withoutExtension) ? "Untitled activity" : withoutExtension;
    }

    /// <summary>Returns null when the stream exceeds <see cref="MaximumFileBytes"/>.</summary>
    private static async Task<byte[]?> ReadBoundedAsync(Stream source, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = ArrayPool<byte>.Shared.Rent(81_920);

        try
        {
            int read;
            while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaximumFileBytes)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        return buffer.ToArray();
    }
}
