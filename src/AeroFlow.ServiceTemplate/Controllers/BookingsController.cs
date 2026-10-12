using AeroFlow.ServiceTemplate.Models;
using Microsoft.AspNetCore.Mvc;

namespace AeroFlow.ServiceTemplate.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class BookingsController : ControllerBase
{
    // Tiny in-memory demo data — replace with real persistence in svc-* repos.
    private static readonly IReadOnlyDictionary<string, BookingSummary> DemoBookings =
        new Dictionary<string, BookingSummary>(StringComparer.OrdinalIgnoreCase)
        {
            ["BK-1001"] = new("BK-1001", "Alex Rivera", "AF204", "Confirmed"),
            ["BK-1002"] = new("BK-1002", "Sam Chen", "AF881", "CheckedIn"),
        };

    /// <summary>Lightweight hello for the booking domain — proves the API is up.</summary>
    [HttpGet("ping")]
    [ProducesResponseType(typeof(BookingPingResponse), StatusCodes.Status200OK)]
    public ActionResult<BookingPingResponse> Ping()
    {
        return Ok(new BookingPingResponse(
            Service: "AeroFlow.ServiceTemplate",
            Message: "Booking probe OK",
            UtcNow: DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Demo lookup. Unknown ids return ProblemDetails (404) via NotFound(),
    /// showing the golden-path error shape without a custom middleware stack.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(BookingSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public ActionResult<BookingSummary> GetById(string id)
    {
        if (!DemoBookings.TryGetValue(id, out var booking))
        {
            return NotFound(new ProblemDetails
            {
                Title = "Booking not found",
                Detail = $"No booking exists with id '{id}'.",
                Status = StatusCodes.Status404NotFound,
                Instance = HttpContext.Request.Path,
            });
        }

        return Ok(booking);
    }
}
