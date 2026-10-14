using System.Net;
using System.Net.Http.Json;
using AeroFlow.BaggageReclaim.Domain;
using AeroFlow.BaggageReclaim.Models;
using AeroFlow.BaggageReclaim.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AeroFlow.BaggageReclaim.Tests.Api;

public sealed class BeltsApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Get_belts_returns_every_belt_with_its_flights()
    {
        var hall = Ok(ReclaimPlanner.Apply(
            DefaultReclaimHall.Create(),
            new AssignBelt(Ok(FlightId.Parse("SIM-7")), Ok(FlightNumber.Parse("BA7")), "AMS", new DateTimeOffset(2026, 10, 14, 18, 0, 0, TimeSpan.Zero), 3)));
        var client = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Baggage:SimulateUnloading", "false");
            b.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IReclaimHallStore>(new InMemoryReclaimHallStore(hall))));
        }).CreateClient();

        var response = await client.GetAsync("/belts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var belts = await response.Content.ReadFromJsonAsync<List<BeltResponse>>();
        Assert.NotNull(belts);
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7"], belts.Select(b => b.Id));
        var reclaim = Assert.Single(belts.SelectMany(b => b.Reclaims));
        Assert.Equal(("SIM-7", "BA7", "1", "Assigned", 3, 0), (reclaim.FlightId, reclaim.FlightNumber, belts.Single(b => b.Reclaims.Count > 0).Id, reclaim.Stage, reclaim.BagCount, reclaim.BagsCollected));
        Assert.Null(reclaim.CompletedAt);
    }

    private static T Ok<T>(Result<T> result) => result.Match(v => v, e => throw new InvalidOperationException(e.Message));
}
