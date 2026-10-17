using System.Net;
using System.Net.Http.Headers;
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

/// <summary>POST /flights/{flightId}/reclaim/complete behind the Ops.Reclaim.Write policy (svc-baggage-reclaim#9, ADR-0018).</summary>
public sealed class ReclaimCompleteApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset Landed = new(2026, 10, 14, 18, 0, 0, TimeSpan.Zero);
    private const string Url = "/flights/SIM-7/reclaim/complete";

    [Fact]
    public async Task Caller_with_reclaim_write_role_completes_the_reclaim()
    {
        var store = Store(Hall());
        var response = await Client(store, "Ops.Reclaim.Write").PostAsync(Url, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reclaim = await response.Content.ReadFromJsonAsync<ReclaimResponse>();
        Assert.Equal(("Complete", 3), (reclaim!.Status, reclaim.Bags.Count));
        Assert.All(reclaim.Bags, b => Assert.Equal("Missing", b.Status));
        Assert.NotNull(reclaim.CompletedAt);

        // The completion event waits in the outbox with the change (ADR-0017), with the deterministic tick id.
        var pending = Assert.Single(store.Pending());
        Assert.Equal((BaggageEventType.ReclaimComplete, 3, 0, 3), (pending.EventType, pending.BagCount, pending.BagsCollected, pending.BagsMissing));
    }

    [Fact]
    public async Task Unauthenticated_caller_gets_401_and_nothing_changes()
    {
        var store = Store(Hall());
        var response = await Client(store, roles: null).PostAsync(Url, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Unloading", ReclaimResponse.StatusName(store.Current().Reclaims.Values.Single().Status));
        Assert.Empty(store.Pending());
    }

    [Fact]
    public async Task Caller_without_the_role_gets_403_and_nothing_changes()
    {
        var store = Store(Hall());
        var response = await Client(store, "Ops.Reclaim.Read").PostAsync(Url, null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Unloading", ReclaimResponse.StatusName(store.Current().Reclaims.Values.Single().Status));
        Assert.Empty(store.Pending());
    }

    [Theory]
    [InlineData("/flights/SIM-404/reclaim/complete", 404, "reclaim_not_found")]
    [InlineData("/flights/bad!id/reclaim/complete", 400, "invalid_flight_id")]
    public async Task Domain_errors_are_problem_details_with_a_code(string path, int status, string code)
    {
        var response = await Client(Store(Hall()), "Ops.Reclaim.Write").PostAsync(path, null);

        Assert.Equal(status, (int)response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(code, problem!.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task Completing_twice_is_a_conflict()
    {
        var client = Client(Store(Hall()), "Ops.Reclaim.Write");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(Url, null)).StatusCode);

        var second = await client.PostAsync(Url, null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("reclaim_closed", problem!.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task Reads_stay_anonymous()
    {
        var reclaim = await Client(Store(Hall()), roles: null).GetFromJsonAsync<ReclaimResponse>("/flights/SIM-7/reclaim");

        Assert.Equal("Unloading", reclaim!.Status);
    }

    [Fact]
    public async Task OpenApi_document_lists_the_complete_endpoint_when_docs_are_enabled()
    {
        var client = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Baggage:SimulateUnloading", "false");
            b.UseSetting("Auth:UseFake", "true");
            b.UseSetting("ApiDocs:Enabled", "true");
        }).CreateClient();

        var json = await client.GetStringAsync("/openapi/v1.json");

        Assert.Contains("/flights/{flightId}/reclaim/complete", json);
    }

    private HttpClient Client(IReclaimHallStore store, string? roles)
    {
        var client = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Baggage:SimulateUnloading", "false");
            b.UseSetting("Auth:UseFake", "true");
            b.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton(store)));
        }).CreateClient();
        if (roles is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Fake", roles);
        }

        return client;
    }

    private static InMemoryReclaimHallStore Store(ReclaimHall hall) => new(hall);

    private static ReclaimHall Hall() => Ok(ReclaimPlanner.Apply(
        DefaultReclaimHall.Create(),
        new AssignBelt(Ok(FlightId.Parse("SIM-7")), Ok(FlightNumber.Parse("BA7")), "AMS", Landed, 3)));

    private static T Ok<T>(Result<T> result) => result.Match(v => v, e => throw new InvalidOperationException(e.Message));
}
