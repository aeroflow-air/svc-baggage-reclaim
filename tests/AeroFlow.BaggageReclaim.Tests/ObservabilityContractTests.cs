using System.Net;
using AeroFlow.ServiceDefaults;
using AeroFlow.ServiceDefaults.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AeroFlow.BaggageReclaim.Tests;

public sealed class ObservabilityContractTests
{
    [Fact]
    public async Task Request_writes_a_platform_http_log_with_required_fields()
    {
        var collector = new LogCollector();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Observability:ServiceName", "AeroFlow.BaggageReclaim.Tests");
            builder.UseSetting("Observability:ServiceVersion", "0.1.0");
            builder.UseSetting("Observability:Environment", "local");
            builder.UseSetting("Observability:Squad", "platform");
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(collector));
        });

        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/belts");
        request.Headers.Add(FlightId.HeaderName, "AF204");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var expected = new ObservabilityResource("AeroFlow.BaggageReclaim.Tests", "0.1.0", "local", "platform");
        collector
            .RequireHttpRequest("GET", "/belts", StatusCodes.Status200OK)
            .RequireRequiredFields(expected)
            .RequireFlightId("AF204");
    }

    [Fact]
    public async Task Alive_and_health_are_up_and_not_request_logged()
    {
        var collector = new LogCollector();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Observability:Squad", "platform");
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(collector));
        });

        var client = factory.CreateClient();
        using var health = await client.GetAsync("/health");
        using var alive = await client.GetAsync("/alive");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
        collector.RequireNoHealthRequestLogs();
    }
}
