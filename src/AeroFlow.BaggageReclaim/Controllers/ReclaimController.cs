using System.Collections.Immutable;
using AeroFlow.BaggageReclaim.Auth;
using AeroFlow.BaggageReclaim.Domain;
using AeroFlow.BaggageReclaim.Models;
using AeroFlow.BaggageReclaim.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AeroFlow.BaggageReclaim.Controllers;

/// <summary>Lookups for a flight's reclaim and for single bags (platform-handbook#69), plus completing a reclaim (svc-baggage-reclaim#9).</summary>
[ApiController]
public sealed class ReclaimController(IReclaimHallStore store, TimeProvider timeProvider) : ControllerBase
{
    /// <summary>A flight's reclaim: its belt, status, stage and every bag.</summary>
    /// <remarks>Cancelled and completed reclaims are returned too, with their status.</remarks>
    /// <param name="flightId">The simulator's flight id, for example <c>SIM-00001</c>.</param>
    /// <response code="200">The reclaim.</response>
    /// <response code="400">The flight id is malformed.</response>
    /// <response code="404">No reclaim for that flight (it has not landed here, or was never assigned a belt).</response>
    [HttpGet("flights/{flightId}/reclaim")]
    [ProducesResponseType(typeof(ReclaimResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public ActionResult<ReclaimResponse> GetReclaim(string flightId) =>
        FlightId.Parse(flightId)
            .Bind(flight => ReclaimQueries.Reclaim(store.Current(), flight))
            .Match<ActionResult>(reclaim => Ok(ReclaimResponse.From(reclaim)), this.Problem);

    /// <summary>Complete a flight's reclaim: close it and mark every uncollected bag missing.</summary>
    /// <remarks>Requires the <c>Ops.Reclaim.Write</c> role (ADR-0018). Publishes <c>ReclaimComplete</c> through the outbox (ADR-0017).</remarks>
    /// <param name="flightId">The simulator's flight id, for example <c>SIM-00001</c>.</param>
    /// <response code="200">The completed reclaim.</response>
    /// <response code="400">The flight id is malformed.</response>
    /// <response code="401">No caller identity.</response>
    /// <response code="403">The caller does not have the <c>Ops.Reclaim.Write</c> role.</response>
    /// <response code="404">No reclaim for that flight.</response>
    /// <response code="409">The reclaim is already complete or cancelled.</response>
    [HttpPost("flights/{flightId}/reclaim/complete")]
    [Authorize(Policy = AuthPolicies.ReclaimWrite)]
    [ProducesResponseType(typeof(ReclaimResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public ActionResult<ReclaimResponse> CompleteReclaim(string flightId) =>
        FlightId.Parse(flightId)
            .Bind(flight => store.UpdateWithEvents(hall => Complete(hall, flight, timeProvider.GetUtcNow()))
                .Bind(change => change.Hall.ReclaimFor(flight).ToResult(() => new ReclaimNotFound(flight))))
            .Match<ActionResult>(reclaim => Ok(ReclaimResponse.From(reclaim)), this.Problem);

    private static Result<HallChange> Complete(ReclaimHall hall, FlightId flight, DateTimeOffset at) =>
        ReclaimPlanner.Apply(hall, new CompleteReclaim(flight, at)).Map(updated =>
        {
            var done = updated.Reclaims[flight];
            var completed = BaggageEvent.For(done, BaggageEventType.ReclaimComplete, at, BaggageEvent.TickId(done, BaggageEventType.ReclaimComplete)) with
            {
                BagCount = done.Bags.Count,
                BagsCollected = done.BagsCollected,
                BagsMissing = done.Bags.Count(b => b.Status == BagStatus.Missing),
            };
            return new HallChange(updated, ImmutableList.Create(completed));
        });

    /// <summary>One bag by its tag, with its flight and belt.</summary>
    /// <param name="bagTag">Bag tag: airline code and six digits, for example <c>BA000123</c>.</param>
    /// <response code="200">The bag.</response>
    /// <response code="400">The tag is malformed.</response>
    /// <response code="404">No bag with that tag.</response>
    [HttpGet("bags/{bagTag}")]
    [ProducesResponseType(typeof(BagResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public ActionResult<BagResponse> GetBag(string bagTag) =>
        BagTag.Parse(bagTag)
            .Bind(tag => ReclaimQueries.Bag(store.Current(), tag))
            .Match<ActionResult>(location => Ok(BagResponse.From(location)), this.Problem);
}
