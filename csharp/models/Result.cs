namespace Dotfiles.Models;

public interface IResult
{
    bool Successful { get; }
}

public abstract record Result<TError> : IResult
{
    private Result(bool successful, TError error)
    {
        Successful = successful;
        Error = error;
    }

    public sealed record Success : Result<TError>
    {
        internal Success() : base(true, default!)
        {
        }
    }

    public sealed record Failure : Result<TError>
    {
        internal Failure(TError error) : base(false, error)
        {
        }
    }

    public abstract record WithValue<TValue> : Result<TError>
    {
        private protected WithValue(bool successful, TError error) : base(successful, error)
        {
        }

        public static implicit operator WithValue<TValue>(TError error) => Fail<TValue>(error);
        public static implicit operator WithValue<TValue>(TValue value) => Ok(value);
    }

    public sealed record Failure<TValue> : WithValue<TValue>
    {
        internal Failure(TError error) : base(false, error)
        {
        }
    }

    public sealed record Success<TValue> : WithValue<TValue>
    {
        internal Success(TValue value) : base(true, default!) => Value = value;
        public TValue Value { get; }
    }

    public bool Successful { get; }

    public TError Error =>
        Successful ? throw new InvalidOperationException("Successful result does not have an error") : field;

    public static Success Ok() => new();
    public static Failure Fail(TError error) => new(error);
    public static implicit operator Result<TError>(TError error) => Fail(error);
    public static Success<TValue> Ok<TValue>(TValue value) => new(value);
    public static Failure<TValue> Fail<TValue>(TError error) => new(error);
}

public enum ErrorCodes
{
    None = 1,
    Invalid = 2,
    NotFound = 3,
    Unknown = 4
}