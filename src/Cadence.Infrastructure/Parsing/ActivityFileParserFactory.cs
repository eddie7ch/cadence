using Cadence.Application.Abstractions;

namespace Cadence.Infrastructure.Parsing;

/// <summary>
/// Picks the parser for an uploaded file.
///
/// The file name is asked first and the bytes second. Extensions are what users
/// and browsers agree on, but they are also the part an upload can most easily
/// get wrong - a file saved as <c>ride.txt</c> is still a FIT file, and its
/// magic bytes say so.
/// </summary>
public sealed class ActivityFileParserFactory : IActivityFileParserFactory
{
    private readonly IActivityFileParser[] _parsers;

    public ActivityFileParserFactory(IEnumerable<IActivityFileParser> parsers)
    {
        ArgumentNullException.ThrowIfNull(parsers);
        _parsers = [.. parsers];

        if (_parsers.Length == 0)
        {
            throw new ArgumentException("At least one activity file parser must be registered.", nameof(parsers));
        }
    }

    /// <exception cref="NotSupportedException">
    /// No registered parser recognises the file. The port returns a parser rather
    /// than a Result, so an unrecognised upload can only be reported by throwing;
    /// callers translate this into a validation failure.
    /// </exception>
    public IActivityFileParser Resolve(string fileName, ReadOnlySpan<byte> header)
    {
        foreach (IActivityFileParser parser in _parsers)
        {
            if (parser.CanParse(fileName, ReadOnlySpan<byte>.Empty))
            {
                return parser;
            }
        }

        foreach (IActivityFileParser parser in _parsers)
        {
            if (parser.CanParse(string.Empty, header))
            {
                return parser;
            }
        }

        string extension = string.IsNullOrEmpty(fileName) ? string.Empty : Path.GetExtension(fileName);
        string described = extension.Length == 0 ? "the uploaded file" : $"\"{extension}\" files";

        throw new NotSupportedException(
            $"No activity file parser recognises {described}. Supported formats: GPX and FIT.");
    }
}
