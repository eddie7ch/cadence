using System.Buffers.Binary;

namespace Cadence.Infrastructure.Parsing.Fit;

/// <summary>
/// Raised when a file is not a FIT file, or is one that cannot be decoded
/// without losing byte alignment. Malformed binary input is a genuine fault, not
/// an expected outcome, so it throws rather than returning a Result.
/// </summary>
public sealed class FitFormatException : Exception
{
    public FitFormatException()
        : base("The FIT file is malformed.")
    {
    }

    public FitFormatException(string message)
        : base(message)
    {
    }

    public FitFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Receives decoded messages. Implemented as a push interface so the decoder can
/// reuse one <see cref="FitFieldSet"/> for the whole file instead of allocating
/// per message; the sink must copy out what it needs before returning.
/// </summary>
internal interface IFitMessageSink
{
    /// <summary>
    /// Answered once per definition message. Uninteresting messages are skipped
    /// by byte count and never decoded.
    /// </summary>
    bool IsInterested(ushort globalMessageNumber);

    void OnMessage(ushort globalMessageNumber, FitFieldSet fields);
}

/// <summary>
/// A from-scratch decoder for the FIT binary protocol.
///
/// The format is self-describing: definition messages declare the layout that
/// later data messages follow, and a data message carries no length of its own.
/// Every byte a definition declares must therefore be consumed exactly, or the
/// stream desynchronises and the remainder of the file silently decodes into
/// plausible-looking garbage. That is why the data-message reader always
/// advances by the declared total size rather than by whatever the field loop
/// happened to read.
/// </summary>
internal static class FitDecoder
{
    private const int MinimumHeaderSize = 12;
    private const int CrcSize = 2;
    private const int MaxLocalMessageTypes = 16;
    private const int MaxCompressedLocalMessageTypes = 4;
    private const uint CompressedTimestampMask = 0x1F;

    private static ReadOnlySpan<byte> Signature => ".FIT"u8;

    public static void Decode(ReadOnlySpan<byte> file, IFitMessageSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        int offset = 0;
        bool decodedAnySection = false;

        while (true)
        {
            if (!TryReadHeader(file, offset, out int headerSize, out uint dataSize, out string? problem))
            {
                if (!decodedAnySection)
                {
                    throw new FitFormatException(problem ?? "The file is not a FIT file.");
                }

                // Trailing bytes after a complete file are padding, not an error.
                return;
            }

            int dataStart = offset + headerSize;
            long dataEnd = (long)dataStart + dataSize;
            if (dataEnd > file.Length)
            {
                throw new FitFormatException(
                    $"The FIT header declares {dataSize} bytes of records but only {file.Length - dataStart} bytes remain.");
            }

            DecodeSection(file.Slice(dataStart, (int)dataSize), sink);
            decodedAnySection = true;

            // Several FIT files may be concatenated into one stream; each has its
            // own header and its own trailing CRC.
            offset = (int)dataEnd + CrcSize;
            if (offset >= file.Length)
            {
                return;
            }
        }
    }

    private static bool TryReadHeader(
        ReadOnlySpan<byte> file,
        int offset,
        out int headerSize,
        out uint dataSize,
        out string? problem)
    {
        headerSize = 0;
        dataSize = 0;
        problem = null;

        if (offset + MinimumHeaderSize > file.Length)
        {
            problem = "The file is shorter than a 12-byte FIT header.";
            return false;
        }

        int declaredSize = file[offset];
        if (declaredSize is not (12 or 14))
        {
            problem = $"Unsupported FIT header size {declaredSize}; expected 12 or 14.";
            return false;
        }

        if (offset + declaredSize > file.Length)
        {
            problem = "The FIT header is truncated.";
            return false;
        }

        if (!file.Slice(offset + 8, 4).SequenceEqual(Signature))
        {
            problem = "The file does not carry the \".FIT\" signature at offset 8.";
            return false;
        }

        // Header size, protocol version and profile version precede the data
        // size; only the data size and the signature matter for decoding.
        dataSize = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(offset + 4, 4));
        headerSize = declaredSize;
        return true;
    }

    private static void DecodeSection(ReadOnlySpan<byte> data, IFitMessageSink sink)
    {
        MessageDefinition?[] definitions = new MessageDefinition?[MaxLocalMessageTypes];
        FitFieldSet fields = new();

        uint referenceTimestamp = 0;
        bool haveReferenceTimestamp = false;

        int position = 0;
        while (position < data.Length)
        {
            byte recordHeader = data[position++];

            if ((recordHeader & 0x80) != 0)
            {
                // Compressed timestamp header: two bits of local message type and
                // five bits of offset from the last full timestamp.
                int local = (recordHeader >> 5) & (MaxCompressedLocalMessageTypes - 1);
                MessageDefinition definition = definitions[local]
                    ?? throw new FitFormatException(
                        $"A compressed-timestamp record referenced undefined local message type {local}.");

                if (!haveReferenceTimestamp)
                {
                    throw new FitFormatException(
                        "A compressed-timestamp record appeared before any full timestamp.");
                }

                referenceTimestamp = ApplyCompressedOffset(
                    referenceTimestamp,
                    recordHeader & CompressedTimestampMask);

                ReadDataMessage(definition, data, ref position, fields, sink, referenceTimestamp);
                continue;
            }

            if ((recordHeader & 0x40) != 0)
            {
                int local = recordHeader & 0x0F;
                definitions[local] = ReadDefinition(data, ref position, (recordHeader & 0x20) != 0, sink);
                continue;
            }

            {
                int local = recordHeader & 0x0F;
                MessageDefinition definition = definitions[local]
                    ?? throw new FitFormatException(
                        $"A data record referenced undefined local message type {local}.");

                uint? timestamp = ReadDataMessage(definition, data, ref position, fields, sink, null);
                if (timestamp.HasValue)
                {
                    referenceTimestamp = timestamp.Value;
                    haveReferenceTimestamp = true;
                }
            }
        }
    }

    private static MessageDefinition ReadDefinition(
        ReadOnlySpan<byte> data,
        ref int position,
        bool hasDeveloperFields,
        IFitMessageSink sink)
    {
        // Reserved byte, architecture, global message number, field count.
        if (position + 5 > data.Length)
        {
            throw new FitFormatException("A definition message is truncated.");
        }

        position++;

        byte architecture = data[position++];
        if (architecture > 1)
        {
            throw new FitFormatException(
                $"Unknown FIT architecture byte 0x{architecture:X2}; expected 0 (little endian) or 1 (big endian).");
        }

        bool bigEndian = architecture == 1;

        ReadOnlySpan<byte> globalBytes = data.Slice(position, 2);
        ushort globalMessageNumber = bigEndian
            ? BinaryPrimitives.ReadUInt16BigEndian(globalBytes)
            : BinaryPrimitives.ReadUInt16LittleEndian(globalBytes);
        position += 2;

        int fieldCount = data[position++];
        if (position + (fieldCount * 3) > data.Length)
        {
            throw new FitFormatException(
                $"A definition message for global message {globalMessageNumber} declares {fieldCount} fields but is truncated.");
        }

        FieldDefinition[] fields = new FieldDefinition[fieldCount];
        int totalSize = 0;
        bool hasTimestampField = false;

        for (int i = 0; i < fieldCount; i++)
        {
            byte number = data[position];
            byte size = data[position + 1];
            byte baseType = data[position + 2];
            position += 3;

            fields[i] = new FieldDefinition(number, size, baseType);
            totalSize += size;

            // Only a four-byte field 253 is the date_time that drives compressed
            // timestamps; anything else with that number is not a reference clock.
            if (number == FitCommonField.Timestamp && FitBaseType.SizeOf(FitBaseType.NumberOf(baseType)) == 4)
            {
                hasTimestampField = true;
            }
        }

        if (hasDeveloperFields)
        {
            if (position >= data.Length)
            {
                throw new FitFormatException(
                    $"A definition message for global message {globalMessageNumber} is truncated before its developer field count.");
            }

            int developerFieldCount = data[position++];
            if (position + (developerFieldCount * 3) > data.Length)
            {
                throw new FitFormatException(
                    $"A definition message for global message {globalMessageNumber} declares {developerFieldCount} developer fields but is truncated.");
            }

            // Developer field values are application-defined and ignored here,
            // but their bytes still occupy the data message and must be counted.
            for (int i = 0; i < developerFieldCount; i++)
            {
                totalSize += data[position + 1];
                position += 3;
            }
        }

        return new MessageDefinition(
            globalMessageNumber,
            bigEndian,
            fields,
            totalSize,
            sink.IsInterested(globalMessageNumber),
            hasTimestampField);
    }

    /// <summary>Returns the message's full timestamp when it carried one.</summary>
    private static uint? ReadDataMessage(
        MessageDefinition definition,
        ReadOnlySpan<byte> data,
        ref int position,
        FitFieldSet fields,
        IFitMessageSink sink,
        uint? compressedTimestamp)
    {
        if (position + definition.TotalSize > data.Length)
        {
            throw new FitFormatException(
                $"A data message for global message {definition.GlobalMessageNumber} is truncated.");
        }

        int start = position;
        uint? timestamp = null;

        // Messages nobody wants are skipped wholesale - except when they carry the
        // reference timestamp that compressed headers are measured against.
        if (definition.IsInteresting || definition.HasTimestampField)
        {
            fields.Clear();

            foreach (FieldDefinition field in definition.Fields)
            {
                FitValue value = FitBaseType.Read(
                    field.BaseType,
                    data.Slice(position, field.Size),
                    definition.BigEndian);
                position += field.Size;

                if (!value.HasValue)
                {
                    continue;
                }

                if (field.Number == FitCommonField.Timestamp
                    && definition.HasTimestampField
                    && value.Integer is >= 0 and <= uint.MaxValue)
                {
                    timestamp = (uint)value.Integer.Value;
                }

                if (definition.IsInteresting)
                {
                    fields.Set(field.Number, value);
                }
            }
        }

        // Always advance by the declared size: this consumes developer fields and
        // guarantees alignment even for definitions whose field sizes disagree
        // with their base types.
        position = start + definition.TotalSize;

        if (compressedTimestamp is { } forced)
        {
            timestamp = forced;
            if (definition.IsInteresting)
            {
                fields.Set(FitCommonField.Timestamp, FitValue.FromInteger(forced));
            }
        }

        if (definition.IsInteresting)
        {
            sink.OnMessage(definition.GlobalMessageNumber, fields);
        }

        return timestamp;
    }

    private static uint ApplyCompressedOffset(uint reference, uint offset)
    {
        // The five-bit offset wraps every 32 seconds; an offset that has gone
        // backwards means one wrap has elapsed.
        uint rolled = offset >= (reference & CompressedTimestampMask)
            ? offset
            : offset + (CompressedTimestampMask + 1);

        return (reference & ~CompressedTimestampMask) + rolled;
    }

    private readonly record struct FieldDefinition(byte Number, byte Size, byte BaseType);

    private sealed record MessageDefinition(
        ushort GlobalMessageNumber,
        bool BigEndian,
        FieldDefinition[] Fields,
        int TotalSize,
        bool IsInteresting,
        bool HasTimestampField);
}
