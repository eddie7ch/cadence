namespace Cadence.Domain.Activities;

public enum Sport
{
    Unknown = 0,
    Running = 1,
    TrailRunning = 2,
    Cycling = 3,
    MountainBiking = 4,
    Swimming = 5,
    Walking = 6,
    Hiking = 7,
    Rowing = 8,
    Skiing = 9,
}

/// <summary>Import lifecycle. Parsing happens off the request thread, so a row exists before it has metrics.</summary>
public enum ActivityStatus
{
    Pending = 0,
    Processing = 1,
    Ready = 2,
    Failed = 3,
}

public enum SourceFormat
{
    Unknown = 0,

    /// <summary>GPS Exchange Format - XML, human readable, widely exported.</summary>
    Gpx = 1,

    /// <summary>Flexible and Interoperable Data Transfer - Garmin's binary format.</summary>
    Fit = 2,

    /// <summary>Training Center XML.</summary>
    Tcx = 3,
}
