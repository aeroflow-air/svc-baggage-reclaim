namespace AeroFlow.BaggageReclaim.Messaging;

/// <summary>Binds the <c>FlightEvents</c> configuration section.</summary>
public sealed class FlightEventsOptions
{
    public const string SectionName = "FlightEvents";

    /// <summary>Aspire connection name; the processor only starts when <c>ConnectionStrings:messaging</c> is set.</summary>
    public const string ConnectionName = "messaging";

    public string Topic { get; set; } = "flight-events";

    /// <summary>The aeroflow-local AppHost creates this subscription and also sets it via <c>FlightEvents__Subscription</c>.</summary>
    public string Subscription { get; set; } = "svc-baggage-reclaim";

    /// <summary>The airport whose reclaim hall this is. Matches the simulator's home airport.</summary>
    public string HomeAirport { get; set; } = "LHR";

    /// <summary>The simulator sends no passenger numbers, so each flight gets a stable bag count in this range.</summary>
    public int MinBagsPerFlight { get; set; } = 60;

    public int MaxBagsPerFlight { get; set; } = 180;
}
