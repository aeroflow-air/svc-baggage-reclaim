using AeroFlow.BaggageReclaim.Domain;
using AeroFlow.BaggageReclaim.Models;
using AeroFlow.BaggageReclaim.Storage;
using Microsoft.AspNetCore.Mvc;

namespace AeroFlow.BaggageReclaim.Controllers;

/// <summary>
/// Read-only HTTP edge for the reclaim hall. Minimal slice of platform-handbook#69 so the ops dashboard can load
/// belts on startup (#74); the rest of #69 (one belt, a flight's reclaim, bags, completion) is still to do.
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
}
