using AeroFlow.BaggageReclaim.Domain;
using AeroFlow.BaggageReclaim.Models;
using AeroFlow.BaggageReclaim.Storage;
using Microsoft.AspNetCore.Mvc;

namespace AeroFlow.BaggageReclaim.Controllers;

/// <summary>Read-only lookups for a flight's reclaim and for single bags (platform-handbook#69).</summary>
[ApiController]
public sealed class ReclaimController(IReclaimHallStore store) : ControllerBase
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
