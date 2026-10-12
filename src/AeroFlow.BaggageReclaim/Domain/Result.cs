using System.Diagnostics;

namespace AeroFlow.BaggageReclaim.Domain;

/// <summary>
/// Explicit outcome of an operation that can fail for an ordinary business reason.
/// Deliberately small (ADR-0004): two sealed cases, <c>Bind</c>/<c>Map</c>/<c>Match</c>, nothing more.
/// </summary>
public abstract record Result<T>
{
    private Result()
    {
    }

    public sealed record Ok(T Value) : Result<T>;

    public sealed record Failure(Error Error) : Result<T>;

    public static implicit operator Result<T>(T value) => new Ok(value);

    public static implicit operator Result<T>(Error error) => new Failure(error);

    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> next) => this switch
    {
        Ok ok => next(ok.Value),
        Failure failure => failure.Error,
        _ => throw new UnreachableException(),
    };

    public Result<TOut> Map<TOut>(Func<T, TOut> map) =>
        Bind(value => (Result<TOut>)new Result<TOut>.Ok(map(value)));

    public TOut Match<TOut>(Func<T, TOut> ok, Func<Error, TOut> failure) => this switch
    {
        Ok success => ok(success.Value),
        Failure error => failure(error.Error),
        _ => throw new UnreachableException(),
    };
}
