using System.Buffers.Binary;
using System.Text;
using Cadence.Domain.Activities;

namespace Cadence.Infrastructure.Parsing.Fit;

/// <summary>
/// One decoded field value. Integer and floating-point values are kept apart
/// because a FIT <c>date_time</c> is a uint32 that must survive round-tripping
/// exactly, while <c>float64</c> fields genuinely are real numbers.
/// A value with nothing set is the "invalid" sentinel the format uses for
/// "this device did not record this field".
/// </summary>
internal readonly record struct FitValue(long? Integer, double? Real, string? Text)
{
    public static FitValue Invalid => default;

    public bool HasValue => Integer.HasValue || Real.HasValue || Text is not null;

    public double? AsDouble
    {
        get
        {
            if (Real.HasValue)
            {
                return Real.Value;
            }

            return Integer.HasValue ? Integer.Value : (double?)null;
        }
    }

    public int? AsInt32
    {
        get
        {
            double? value = AsDouble;
            if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            {
                return null;
            }

            double rounded = Math.Round(value.Value, MidpointRounding.AwayFromZero);
            return rounded is >= int.MinValue and <= int.MaxValue ? (int)rounded : null;
        }
    }

    public static FitValue FromInteger(long value) => new(value, null, null);

    public static FitValue FromReal(double value) => new(null, value, null);

    public static FitValue FromText(string value) => new(null, null, value);
}

/// <summary>
/// The fields of a single decoded message, indexed by field definition number.
///
/// A three-hour ride holds tens of thousands of record messages, so the decoder
/// reuses one instance for the whole file rather than allocating a dictionary
/// per message. A sink must therefore read what it needs during the callback and
/// never hold on to the instance.
/// </summary>
internal sealed class FitFieldSet
{
    private const int Capacity = 256;

    private readonly FitValue[] _values = new FitValue[Capacity];
    private readonly bool[] _present = new bool[Capacity];

    public void Clear()
    {
        Array.Clear(_values);
        Array.Clear(_present);
    }

    public void Set(byte number, FitValue value)
    {
        _values[number] = value;
        _present[number] = true;
    }

    public double? GetDouble(byte number) => _present[number] ? _values[number].AsDouble : null;

    public int? GetInt32(byte number) => _present[number] ? _values[number].AsInt32 : null;

    public long? GetInt64(byte number) => _present[number] ? _values[number].Integer : null;

    public string? GetString(byte number) => _present[number] ? _values[number].Text : null;

    /// <summary>First of <paramref name="numbers"/> that carries a value, in the order given.</summary>
    public double? GetFirstDouble(params byte[] numbers)
    {
        ArgumentNullException.ThrowIfNull(numbers);
        foreach (byte number in numbers)
        {
            double? value = GetDouble(number);
            if (value.HasValue)
            {
                return value;
            }
        }

        return null;
    }
}

/// <summary>
/// The FIT base types, addressed by the low five bits of a field's base type
/// byte. Bit 7 of that byte only says whether the type is endian-sensitive,
/// which the size already tells us, so it is masked off.
/// </summary>
internal static class FitBaseType
{
    public const int StringNumber = 7;

    private const int MaxNumber = 16;

    // Indexed by base type number: enum, sint8, uint8, sint16, uint16, sint32,
    // uint32, string, float32, float64, uint8z, uint16z, uint32z, byte, sint64,
    // uint64, uint64z.
    private static readonly byte[] Sizes = [1, 1, 1, 2, 2, 4, 4, 1, 4, 8, 1, 2, 4, 1, 8, 8, 8];

    public static int SizeOf(int baseTypeNumber) =>
        (uint)baseTypeNumber <= MaxNumber ? Sizes[baseTypeNumber] : 0;

    public static int NumberOf(byte baseTypeByte) => baseTypeByte & 0x1F;

    /// <summary>
    /// Decodes the first element of a field. Arrays are declared by a size that
    /// is a multiple of the base size; callers still advance by the declared
    /// size, so nothing here affects stream alignment.
    /// </summary>
    public static FitValue Read(byte baseTypeByte, ReadOnlySpan<byte> bytes, bool bigEndian)
    {
        int number = NumberOf(baseTypeByte);

        if (number == StringNumber)
        {
            return ReadString(bytes);
        }

        int size = SizeOf(number);

        // An unknown base type, or a field too small to hold its own base type,
        // is not decodable - but it is still consumed by the caller.
        if (size == 0 || bytes.Length < size)
        {
            return FitValue.Invalid;
        }

        ReadOnlySpan<byte> element = bytes[..size];

        switch (number)
        {
            case 0: // enum
            case 2: // uint8
            case 13: // byte
                return element[0] == 0xFF ? FitValue.Invalid : FitValue.FromInteger(element[0]);

            case 1: // sint8
            {
                sbyte value = unchecked((sbyte)element[0]);
                return value == sbyte.MaxValue ? FitValue.Invalid : FitValue.FromInteger(value);
            }

            case 3: // sint16
            {
                short value = bigEndian
                    ? BinaryPrimitives.ReadInt16BigEndian(element)
                    : BinaryPrimitives.ReadInt16LittleEndian(element);
                return value == short.MaxValue ? FitValue.Invalid : FitValue.FromInteger(value);
            }

            case 4: // uint16
            {
                ushort value = ReadUInt16(element, bigEndian);
                return value == ushort.MaxValue ? FitValue.Invalid : FitValue.FromInteger(value);
            }

            case 5: // sint32
            {
                int value = bigEndian
                    ? BinaryPrimitives.ReadInt32BigEndian(element)
                    : BinaryPrimitives.ReadInt32LittleEndian(element);
                return value == int.MaxValue ? FitValue.Invalid : FitValue.FromInteger(value);
            }

            case 6: // uint32
            {
                uint value = ReadUInt32(element, bigEndian);
                return value == uint.MaxValue ? FitValue.Invalid : FitValue.FromInteger(value);
            }

            case 8: // float32
            {
                float value = bigEndian
                    ? BinaryPrimitives.ReadSingleBigEndian(element)
                    : BinaryPrimitives.ReadSingleLittleEndian(element);

                // The float invalid sentinel is all-bits-set, which is a NaN.
                return float.IsNaN(value) ? FitValue.Invalid : FitValue.FromReal(value);
            }

            case 9: // float64
            {
                double value = bigEndian
                    ? BinaryPrimitives.ReadDoubleBigEndian(element)
                    : BinaryPrimitives.ReadDoubleLittleEndian(element);
                return double.IsNaN(value) ? FitValue.Invalid : FitValue.FromReal(value);
            }

            case 10: // uint8z
                return element[0] == 0 ? FitValue.Invalid : FitValue.FromInteger(element[0]);

            case 11: // uint16z
            {
                ushort value = ReadUInt16(element, bigEndian);
                return value == 0 ? FitValue.Invalid : FitValue.FromInteger(value);
            }

            case 12: // uint32z
            {
                uint value = ReadUInt32(element, bigEndian);
                return value == 0 ? FitValue.Invalid : FitValue.FromInteger(value);
            }

            case 14: // sint64
            {
                long value = bigEndian
                    ? BinaryPrimitives.ReadInt64BigEndian(element)
                    : BinaryPrimitives.ReadInt64LittleEndian(element);
                return value == long.MaxValue ? FitValue.Invalid : FitValue.FromInteger(value);
            }

            case 15: // uint64
            {
                ulong value = ReadUInt64(element, bigEndian);
                return value == ulong.MaxValue ? FitValue.Invalid : FromUnsigned64(value);
            }

            case 16: // uint64z
            {
                ulong value = ReadUInt64(element, bigEndian);
                return value == 0 ? FitValue.Invalid : FromUnsigned64(value);
            }

            default:
                return FitValue.Invalid;
        }
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, bool bigEndian) => bigEndian
        ? BinaryPrimitives.ReadUInt16BigEndian(bytes)
        : BinaryPrimitives.ReadUInt16LittleEndian(bytes);

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, bool bigEndian) => bigEndian
        ? BinaryPrimitives.ReadUInt32BigEndian(bytes)
        : BinaryPrimitives.ReadUInt32LittleEndian(bytes);

    private static ulong ReadUInt64(ReadOnlySpan<byte> bytes, bool bigEndian) => bigEndian
        ? BinaryPrimitives.ReadUInt64BigEndian(bytes)
        : BinaryPrimitives.ReadUInt64LittleEndian(bytes);

    private static FitValue FromUnsigned64(ulong value) =>
        value <= long.MaxValue ? FitValue.FromInteger((long)value) : FitValue.FromReal(value);

    private static FitValue ReadString(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        ReadOnlySpan<byte> text = end >= 0 ? bytes[..end] : bytes;
        if (text.IsEmpty)
        {
            return FitValue.Invalid;
        }

        string decoded = Encoding.UTF8.GetString(text).Trim();
        return decoded.Length == 0 ? FitValue.Invalid : FitValue.FromText(decoded);
    }
}

internal static class FitGlobalMessage
{
    public const ushort FileId = 0;
    public const ushort Sport = 12;
    public const ushort Session = 18;
    public const ushort Lap = 19;
    public const ushort Record = 20;
    public const ushort Activity = 34;
}

/// <summary>Field numbers shared by every message that carries them.</summary>
internal static class FitCommonField
{
    public const byte Timestamp = 253;
}

internal static class FitFileIdField
{
    public const byte Type = 0;
    public const byte Manufacturer = 1;
    public const byte Product = 2;
    public const byte ProductName = 8;
}

internal static class FitRecordField
{
    public const byte PositionLat = 0;
    public const byte PositionLong = 1;
    public const byte Altitude = 2;
    public const byte HeartRate = 3;
    public const byte Cadence = 4;
    public const byte Distance = 5;
    public const byte Speed = 6;
    public const byte Power = 7;
    public const byte Temperature = 13;
    public const byte EnhancedSpeed = 73;
    public const byte EnhancedAltitude = 78;
}

internal static class FitSessionField
{
    public const byte Sport = 5;
    public const byte SubSport = 6;
}

internal static class FitLapField
{
    public const byte Sport = 25;
    public const byte SubSport = 39;
}

internal static class FitSportField
{
    public const byte Sport = 0;
    public const byte SubSport = 1;
    public const byte Name = 3;
}

internal static class FitActivityField
{
    public const byte LocalTimestamp = 5;
}

internal static class FitEpoch
{
    /// <summary>
    /// FIT counts seconds from 1989-12-31T00:00:00Z, not from the Unix epoch.
    /// Using the wrong origin puts every activity twenty years out.
    /// </summary>
    public static readonly DateTimeOffset Origin = new(1989, 12, 31, 0, 0, 0, TimeSpan.Zero);

    public static DateTimeOffset? ToDateTimeOffset(double? secondsSinceOrigin)
    {
        if (secondsSinceOrigin is not { } seconds || double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return null;
        }

        // Guard the DateTimeOffset range rather than letting a corrupt field throw.
        if (seconds is < 0 or > 4_102_444_800)
        {
            return null;
        }

        return Origin.AddSeconds(seconds);
    }
}

internal static class FitSportMapper
{
    private const int RunningSubSportTrail = 3;
    private const int CyclingSubSportDownhill = 9;
    private const int CyclingSubSportMountain = 8;
    private const int CyclingSubSportCyclocross = 11;

    /// <summary>
    /// Maps the FIT sport/sub-sport pair onto the domain enum. The table is
    /// deliberately partial: the profile defines fifty sports and guessing at a
    /// bucket for kitesurfing is worse than reporting Unknown.
    /// </summary>
    public static Sport Map(int? sport, int? subSport)
    {
        if (sport is not { } value)
        {
            return Sport.Unknown;
        }

        return value switch
        {
            1 => subSport == RunningSubSportTrail ? Sport.TrailRunning : Sport.Running,
            2 or 21 => subSport is CyclingSubSportMountain or CyclingSubSportDownhill or CyclingSubSportCyclocross
                ? Sport.MountainBiking
                : Sport.Cycling,
            5 => Sport.Swimming,
            11 => Sport.Walking,
            12 or 13 => Sport.Skiing,
            15 => Sport.Rowing,
            16 or 17 => Sport.Hiking,
            _ => Sport.Unknown,
        };
    }
}

internal static class FitManufacturer
{
    /// <summary>
    /// Only the manufacturer ids worth being confident about. An unrecognised id
    /// renders as its number, because a wrong brand name on someone's activity
    /// is worse than an honest "Manufacturer 42".
    /// </summary>
    public static string? Name(long? manufacturerId) => manufacturerId switch
    {
        1 => "Garmin",
        3 => "Zephyr",
        6 => "SRM",
        7 => "Quarq",
        13 or 15 => "Dynastream",
        16 => "Timex",
        23 => "Suunto",
        32 => "Wahoo Fitness",
        40 => "Concept2",
        95 => "Stages Cycling",
        _ => null,
    };
}
