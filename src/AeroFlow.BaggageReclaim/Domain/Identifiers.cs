namespace AeroFlow.BaggageReclaim.Domain;

/// <summary>Flight number such as <c>BA123</c>. Two-character airline designator then 1-4 digits (same as flight status).</summary>
public sealed record FlightNumber
{
    private FlightNumber(string value) => Value = value;

    public string Value { get; }

    /// <summary>The two-character airline designator, e.g. <c>BA</c> or <c>U2</c>.</summary>
    public string Airline => Value[..2];

    public static Result<FlightNumber> Parse(string? value) =>
        value?.Trim().ToUpperInvariant() is { Length: >= 3 and <= 6 } v
        && v[..2].All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c))
        && v[..2].Any(char.IsAsciiLetterUpper)
        && v[2..].All(char.IsAsciiDigit)
            ? new FlightNumber(v)
            : new InvalidFlightNumber(value);

    public override string ToString() => Value;
}

/// <summary>The simulator's flight id (e.g. <c>SIM-00001</c>); the key for a reclaim.</summary>
public sealed record FlightId
{
    private FlightId(string value) => Value = value;

    public string Value { get; }

    public static Result<FlightId> Parse(string? value) =>
        value?.Trim() is { Length: >= 1 and <= 64 } v && v.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? new FlightId(v)
            : new InvalidFlightId(value);

    public override string ToString() => Value;
}

/// <summary>Reclaim belt such as <c>1</c> or <c>B4</c>.</summary>
public sealed record BeltId
{
    private BeltId(string value) => Value = value;

    public string Value { get; }

    public static Result<BeltId> Parse(string? value) =>
        value?.Trim().ToUpperInvariant() is { Length: >= 1 and <= 3 } v && v.All(char.IsAsciiLetterOrDigit)
            ? new BeltId(v)
            : new InvalidBeltId(value);

    public override string ToString() => Value;
}

/// <summary>Bag tag: airline designator plus a six-digit serial, e.g. <c>BA000123</c>.</summary>
public sealed record BagTag
{
    private BagTag(string value) => Value = value;

    public string Value { get; }

    public static Result<BagTag> Parse(string? value) =>
        value?.Trim().ToUpperInvariant() is { Length: 8 } v
        && v[..2].All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c))
        && v[2..].All(char.IsAsciiDigit)
            ? new BagTag(v)
            : new InvalidBagTag(value);

    /// <summary>Tag for the <paramref name="serial"/>th bag of an airline; serial is wrapped into six digits.</summary>
    public static BagTag For(FlightNumber flight, int serial) => new($"{flight.Airline}{serial % 1_000_000:D6}");

    public override string ToString() => Value;
}
