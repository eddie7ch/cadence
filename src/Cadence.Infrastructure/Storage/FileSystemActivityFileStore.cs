using Cadence.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cadence.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string UploadDirectory { get; set; } = "/data/uploads";
}

/// <summary>
/// Content-addressed storage on a mounted volume.
///
/// Layout is <c>{root}/{athleteId}/{checksum}{extension}</c>. Naming by checksum
/// rather than by the uploaded file name means a re-upload overwrites itself
/// byte-for-byte instead of accumulating "Morning Run (3).gpx", and it makes the
/// path derivable from the activity row alone - which is what lets the background
/// worker reopen a file without the request that uploaded it.
///
/// Object storage is the production answer; this is deliberately the simplest
/// thing that satisfies the port, and swapping it is a DI change.
/// </summary>
public sealed class FileSystemActivityFileStore : IActivityFileStore
{
    private readonly string _root;
    private readonly ILogger<FileSystemActivityFileStore> _logger;

    public FileSystemActivityFileStore(
        IOptions<StorageOptions> options,
        ILogger<FileSystemActivityFileStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _root = string.IsNullOrWhiteSpace(options.Value.UploadDirectory)
            ? "/data/uploads"
            : options.Value.UploadDirectory;
        _logger = logger;
    }

    public async Task SaveAsync(
        Guid athleteId,
        string checksum,
        string fileName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        string path = ResolvePath(athleteId, checksum, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write to a temporary name and move into place, so a crash mid-write
        // cannot leave a truncated file that looks complete to the worker.
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, content, cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public Task<Stream?> OpenAsync(
        Guid athleteId,
        string checksum,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string path = ResolvePath(athleteId, checksum, fileName);
        if (!File.Exists(path))
        {
            _logger.LogWarning("Stored upload missing at {Path}.", path);
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            useAsync: true);

        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(
        Guid athleteId,
        string checksum,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryDelete(ResolvePath(athleteId, checksum, fileName));
        return Task.CompletedTask;
    }

    /// <remarks>
    /// The checksum is validated as hex rather than trusted. It reaches here from
    /// a hash the application computed, but a path component assembled from data
    /// is a traversal waiting to happen, and the check costs nothing.
    /// </remarks>
    private string ResolvePath(Guid athleteId, string checksum, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checksum);

        foreach (char c in checksum)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                throw new ArgumentException("Checksum must be hexadecimal.", nameof(checksum));
            }
        }

        string extension = Path.GetExtension(fileName ?? string.Empty);
        if (extension.Length > 16 || extension.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.'))
        {
            extension = string.Empty;
        }

        return Path.Combine(_root, athleteId.ToString("N"), checksum + extension);
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            // A file we cannot remove is wasted disk, not a failed request.
            _logger.LogWarning(ex, "Could not delete {Path}.", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Not permitted to delete {Path}.", path);
        }
    }
}
