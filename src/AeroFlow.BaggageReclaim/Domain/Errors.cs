namespace AeroFlow.BaggageReclaim.Domain;

/// <summary>
/// Typed failure with a stable machine-readable <see cref="Code"/> and a human message.
/// Three families, which the HTTP edge maps to 400 / 404 / 409 respectively (ADR-0004).
/// </summary>
public abstract record Error(string Code, string Message);

/// <summary>Input was malformed or failed a value rule (maps to 400).</summary>
public abstract record ValidationError(string Code, string Message) : Error(Code, Message);

/// <summary>Something the request refers to does not exist (maps to 404).</summary>
public abstract record NotFoundError(string Code, string Message) : Error(Code, Message);

/// <summary>Input was well formed but breaks a reclaim rule (maps to 409).</summary>
public abstract record RuleViolation(string Code, string Message) : Error(Code, Message);

// --- Validation -----------------------------------------------------------------

public sealed record InvalidFlightNumber(string? Value)
    : ValidationError("invalid_flight_number", $"'{Value}' is not a valid flight number (e.g. BA123).");

public sealed record InvalidFlightId(string? Value)
    : ValidationError("invalid_flight_id", $"'{Value}' is not a valid flight id.");

public sealed record InvalidBeltId(string? Value)
    : ValidationError("invalid_belt", $"'{Value}' is not a valid belt (expected 1-3 letters or digits).");

public sealed record InvalidBagTag(string? Value)
    : ValidationError("invalid_bag_tag", $"'{Value}' is not a valid bag tag (expected airline code and 6 digits, e.g. BA000123).");

public sealed record InvalidBagCount(int Value)
    : ValidationError("invalid_bag_count", $"Bag count {Value} must be between 0 and {ReclaimRules.MaxBagsPerFlight}.");

public sealed record InvalidBeltCapacity(int Value)
    : ValidationError("invalid_belt_capacity", $"Belt capacity {Value} must be at least 1.");

public sealed record DuplicateBelt(BeltId Belt)
    : ValidationError("duplicate_belt", $"Belt {Belt} is listed more than once.");

public sealed record NoBelts()
    : ValidationError("no_belts", "A reclaim hall needs at least one belt.");

// --- Not found ------------------------------------------------------------------

public sealed record ReclaimNotFound(FlightId Flight)
    : NotFoundError("reclaim_not_found", $"No reclaim exists for flight '{Flight}'.");

public sealed record BagNotFound(BagTag Bag)
    : NotFoundError("bag_not_found", $"No bag exists with tag '{Bag}'.");

public sealed record BeltNotFound(BeltId Belt)
    : NotFoundError("belt_not_found", $"No belt exists with id '{Belt}'.");

// --- Rule violations ------------------------------------------------------------

public sealed record AlreadyAssigned(FlightId Flight, BeltId Belt)
    : RuleViolation("already_assigned", $"Flight '{Flight}' is already on belt {Belt}.");

public sealed record NoBeltAvailable(FlightNumber Flight)
    : RuleViolation("no_belt_available", $"Every belt is full; flight {Flight} cannot be assigned.");

public sealed record ReclaimAlreadyClosed(FlightId Flight)
    : RuleViolation("reclaim_closed", $"Reclaim for flight '{Flight}' is already complete or cancelled.");

public sealed record InvalidBagTransition(BagTag Bag, BagStatus From, BagStatus To)
    : RuleViolation("invalid_bag_transition", $"Bag {Bag} cannot go from {From} to {To}.");
