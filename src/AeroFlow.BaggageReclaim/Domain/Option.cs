using System.Diagnostics;

namespace AeroFlow.BaggageReclaim.Domain;

/// <summary>
/// A value that may be absent, used in the domain instead of <c>null</c> (ADR-0004).
/// Two sealed cases; <c>Map</c>/<c>Bind</c>/<c>Match</c> plus <see cref="ToResult"/> to turn absence into a typed error.
/// Nullable types appear only at the HTTP edge, where JSON needs them.
/// </summary>
public abstract record Option<T>
{
    private Option()
    {
    }

    public sealed record Some(T Value) : Option<T>;

    public sealed record None : Option<T>
    {
        internal static readonly None Instance = new();
    }

    public Option<TOut> Bind<TOut>(Func<T, Option<TOut>> next) => this switch
    {
        Some some => next(some.Value),
        None => Option.None<TOut>(),
        _ => throw new UnreachableException(),
    };

    public Option<TOut> Map<TOut>(Func<T, TOut> map) =>
        Bind(value => Option.Some(map(value)));

    public TOut Match<TOut>(Func<T, TOut> some, Func<TOut> none) => this switch
    {
        Some value => some(value.Value),
        None => none(),
        _ => throw new UnreachableException(),
    };

    /// <summary>Absence becomes the given error; presence becomes <c>Ok</c>.</summary>
    public Result<T> ToResult(Func<Error> whenNone) =>
        Match(value => (Result<T>)value, () => whenNone());
}

public static class Option
{
    public static Option<T> Some<T>(T value) => new Option<T>.Some(value);

    public static Option<T> None<T>() => Option<T>.None.Instance;

    /// <summary>First element matching <paramref name="predicate"/>, or <c>None</c>.</summary>
    public static Option<T> FirstOrNone<T>(this IEnumerable<T> source, Func<T, bool> predicate)
    {
        foreach (var item in source)
        {
            if (predicate(item))
            {
                return Some(item);
            }
        }

        return None<T>();
    }

    public static Option<T> FirstOrNone<T>(this IEnumerable<T> source) => source.FirstOrNone(_ => true);

    /// <summary>Dictionary lookup that says "maybe" in the type rather than via an out parameter.</summary>
    public static Option<TValue> Find<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> source, TKey key) =>
        source.TryGetValue(key, out var value) ? Some(value) : None<TValue>();
}
