namespace AeroFlow.ServiceTemplate.Models;

public sealed record BookingPingResponse(
    string Service,
    string Message,
    DateTimeOffset UtcNow);
