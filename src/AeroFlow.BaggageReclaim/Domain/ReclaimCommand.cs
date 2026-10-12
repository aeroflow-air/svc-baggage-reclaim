namespace AeroFlow.BaggageReclaim.Domain;

/// <summary>A change to the reclaim hall. Applied by <see cref="ReclaimPlanner.Apply"/>.</summary>
public abstract record ReclaimCommand
{
    private protected ReclaimCommand()
    {
    }

    public string Name => GetType().Name;
}

/// <summary>A flight has landed: put it on the least busy belt with room and create its bags as unloaded.</summary>
public sealed record AssignBelt(FlightId Flight, FlightNumber FlightNumber, string Origin, DateTimeOffset LandedAt, int BagCount) : ReclaimCommand;

/// <summary>An arriving flight is late. Recorded on its reclaim if one exists.</summary>
public sealed record RecordDelay(FlightId Flight, TimeSpan Delay) : ReclaimCommand;

/// <summary>A flight is cancelled: close its reclaim and free the belt slot.</summary>
public sealed record CancelReclaim(FlightId Flight, string Reason) : ReclaimCommand;

/// <summary>Move one bag to a new status (on belt, collected, delayed, missing).</summary>
public sealed record MoveBag(BagTag Bag, BagStatus To) : ReclaimCommand;

/// <summary>Close a reclaim; bags not collected by then are marked missing.</summary>
public sealed record CompleteReclaim(FlightId Flight, DateTimeOffset At) : ReclaimCommand;
