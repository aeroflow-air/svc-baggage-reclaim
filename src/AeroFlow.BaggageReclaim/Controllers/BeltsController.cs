using AeroFlow.BaggageReclaim.Domain;
using AeroFlow.BaggageReclaim.Models;
using AeroFlow.BaggageReclaim.Storage;
using Microsoft.AspNetCore.Mvc;

namespace AeroFlow.BaggageReclaim.Controllers;

/// <summary>
/// Read-only HTTP edge for the reclaim hall (platform-handbook#69). Decisions live in <see cref="ReclaimQueries"/>;
/// domain errors become ProblemDetails.
/// </summary>
[ApiController]
[Route("belts")]
public sealed class BeltsController(IReclaimHallStore store) : ControllerBase
{
    /// <summary>Every belt with the flights on it, in hall order.</summary>
    /// <response code="200">Every belt with its non-cancelled reclaims, earliest landing first.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BeltResponse>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<BeltResponse>> GetAll() =>
        Ok(ReclaimQueries.Belts(store.Current()).ConvertAll(BeltResponse.From));

    /// <summary>One belt with the flights on it.</summary>
    /// <param name="id">Belt id, for example <c>3</c>.</param>
    /// <response code="200">The belt with its non-cancelled reclaims, earliest landing first.</response>
    /// <response code="400">The id is not a valid belt id.</response>
    /// <response code="404">No belt with that id.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(BeltResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public ActionResult<BeltResponse> GetById(string id) =>
        BeltId.Parse(id)
            .Bind(belt => ReclaimQueries.Belt(store.Current(), belt))
            .Match<ActionResult>(view => Ok(BeltResponse.From(view)), this.Problem);
}
