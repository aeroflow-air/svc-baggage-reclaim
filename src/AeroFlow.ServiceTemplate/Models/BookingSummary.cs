namespace AeroFlow.ServiceTemplate.Models;

public sealed record BookingSummary(
    string Id,
    string PassengerName,
    string FlightNumber,
    string Status);
