namespace Cadence.Application.Common;

/// <summary>
/// The class of failure, deliberately transport-agnostic. The API layer maps
/// these to status codes; nothing below it knows what HTTP is.
/// </summary>
public enum ErrorKind
{
    None = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Forbidden = 4,
    Unprocessable = 5,
    Unavailable = 6,
}

public sealed record Error(ErrorKind Kind, string Message)
{
    public static Error Validation(string message) => new(ErrorKind.Validation, message);

    public static Error NotFound(string message) => new(ErrorKind.NotFound, message);

    public static Error Conflict(string message) => new(ErrorKind.Conflict, message);

    public static Error Forbidden(string message) => new(ErrorKind.Forbidden, message);

    public static Error Unprocessable(string message) => new(ErrorKind.Unprocessable, message);

    public static Error Unavailable(string message) => new(ErrorKind.Unavailable, message);
}

/// <summary>
/// Expected failures are values, not exceptions. "This athlete does not own that
/// activity" is a normal outcome of a request, and modelling it as a thrown
/// exception makes it invisible in a method signature and expensive at runtime.
/// Genuine faults still throw.
/// </summary>
public readonly record struct Result<T>
{
    private Result(T value)
    {
        Value = value;
        Error = null;
    }

    private Result(Error error)
    {
        Value = default;
        Error = error;
    }

    public T? Value { get; }

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(Error error) => new(error);

    public Result<TOut> Map<TOut>(Func<T, TOut> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return IsSuccess ? Result<TOut>.Success(projection(Value!)) : Result<TOut>.Failure(Error!);
    }

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess(Value!) : onFailure(Error!);
    }
}

/// <summary>Void-returning equivalent of <see cref="Result{T}"/>.</summary>
public readonly record struct Result
{
    private Result(Error? error) => Error = error;

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public static Result Success() => new(null);

    public static Result Failure(Error error) => new(error);

    public static implicit operator Result(Error error) => new(error);
}
