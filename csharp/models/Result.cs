namespace Dotfiles.Models;

public readonly union Result<TError>(Result<TError>.Failure, Result<TError>.Success) {
    public sealed record Failure(TError Error);
    public sealed record Success;
    public sealed record HasValue<TValue>(TValue Value);

    public readonly union WithValue<TValue>(Failure, HasValue<TValue>) {
        public TError? Error => this is Failure failure ? failure.Error : default;

        public static implicit operator WithValue<TValue>(TError error) => Fail(error);
        public static implicit operator WithValue<TValue>(TValue value) => new HasValue<TValue>(value);
    }

    public static Success Ok() => new();
    public static Failure Fail(TError error) => new(error);

    public static implicit operator Result<TError>(TError error) => Fail(error);
}
