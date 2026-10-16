using System.Net;
using System.Net.Http.Json;
using AeroFlow.BaggageReclaim.Domain;
using AeroFlow.BaggageReclaim.Models;
using AeroFlow.BaggageReclaim.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AeroFlow.BaggageReclaim.Tests.Api;

public sealed class ReclaimApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset Landed = new(2026, 10, 14, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Get_belt_returns_that_belt()
    {
        var response = await Client(Hall()).GetAsync("/belts/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var belt = await response.Content.ReadFromJsonAsync<BeltResponse>();
        Assert.Equal(("1", "SIM-7"), (belt!.Id, Assert.Single(belt.Reclaims).FlightId));
    }

    [Theory]
    [InlineData("/belts/99", 404, "belt_not_found")]
    [InlineData("/belts/not-a-belt", 400, "invalid_belt")]
    [InlineData("/flights/SIM-404/reclaim", 404, "reclaim_not_found")]
    [InlineData("/flights/bad!id/reclaim", 400, "invalid_flight_id")]
    [InlineData("/bags/BA999999", 404, "bag_not_found")]
    [InlineData("/bags/nope", 400, "invalid_bag_tag")]
    public async Task Errors_are_problem_details_with_a_code(string path, int status, string code)
    {
        var response = await Client(Hall()).GetAsync(path);

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(code, problem!.Extensions["code"]?.ToString());
        Assert.Equal(path, problem.Instance);
    }

    [Fact]
    public async Task Get_flight_reclaim_returns_belt_status_and_bags()
    {
        var response = await Client(Hall()).GetAsync("/flights/SIM-7/reclaim");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reclaim = await response.Content.ReadFromJsonAsync<ReclaimResponse>();
        Assert.Equal(("SIM-7", "BA7", "1", "Unloading", "Assigned", 3), (reclaim!.FlightId, reclaim.FlightNumber, reclaim.BeltId, reclaim.Status, reclaim.Stage, reclaim.Bags.Count));
        Assert.All(reclaim.Bags, b => Assert.Equal("Unloaded", b.Status));
        Assert.Null(reclaim.CancelReason);
    }

    [Fact]
    public async Task Get_cancelled_reclaim_shows_the_reason()
    {
        var hall = Ok(ReclaimPlanner.Apply(Hall(), new CancelReclaim(Ok(FlightId.Parse("SIM-7")), "diverted")));

        var reclaim = await Client(hall).GetFromJsonAsync<ReclaimResponse>("/flights/SIM-7/reclaim");

        Assert.Equal(("Cancelled", "diverted"), (reclaim!.Status, reclaim.CancelReason));
    }

    [Fact]
    public async Task Get_bag_returns_the_bag_with_its_flight_and_belt()
    {
        var hall = Hall();
        var tag = hall.Reclaims.Values.Single().Bags[0].Tag;
        hall = Ok(ReclaimPlanner.Apply(hall, new MoveBag(tag, BagStatus.OnBelt)));

        var bag = await Client(hall).GetFromJsonAsync<BagResponse>($"/bags/{tag.Value.ToLowerInvariant()}");

        Assert.Equal((tag.Value, "OnBelt", "SIM-7", "BA7", "1", "Unloading"), (bag!.Tag, bag.Status, bag.FlightId, bag.FlightNumber, bag.BeltId, bag.ReclaimStatus));
    }

    [Fact]
    public async Task OpenApi_document_lists_the_endpoints_when_docs_are_enabled()
    {
        var client = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Baggage:SimulateUnloading", "false");
            b.UseSetting("ApiDocs:Enabled", "true");
        }).CreateClient();

        var json = await client.GetStringAsync("/openapi/v1.json");

        Assert.Contains("/belts/{id}", json);
        Assert.Contains("/flights/{flightId}/reclaim", json);
        Assert.Contains("/bags/{bagTag}", json);
    }

    private HttpClient Client(ReclaimHall hall) =>
        factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Baggage:SimulateUnloading", "false");
            b.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IReclaimHallStore>(new InMemoryReclaimHallStore(hall))));
        }).CreateClient();

    private static ReclaimHall Hall() => Ok(ReclaimPlanner.Apply(
        DefaultReclaimHall.Create(),
        new AssignBelt(Ok(FlightId.Parse("SIM-7")), Ok(FlightNumber.Parse("BA7")), "AMS", Landed, 3)));

    private static T Ok<T>(Result<T> result) => result.Match(v => v, e => throw new InvalidOperationException(e.Message));
}
