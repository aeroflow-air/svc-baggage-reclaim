using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AeroFlow.BaggageReclaim.Tests.Api;

/// <summary>
/// Keeps openapi/svc-baggage-reclaim.json (catalogued by Backstage, ADR-0024) equal to the document the
/// service really serves. After an API change, regenerate with
/// <c>UPDATE_OPENAPI=1 dotnet test --filter OpenApiExportTests</c> and commit the file.
/// </summary>
public sealed class OpenApiExportTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Checked_in_openapi_file_matches_the_generated_document()
    {
        var client = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Baggage:SimulateUnloading", "false");
            b.UseSetting("ApiDocs:Enabled", "true");
        }).CreateClient();

        var generated = Normalise(await client.GetStringAsync("/openapi/v1.json"));
        var path = SpecPath();

        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, generated);
            return;
        }

        Assert.True(File.Exists(path), $"{path} is missing. Run UPDATE_OPENAPI=1 dotnet test --filter OpenApiExportTests.");
        Assert.True(
            Normalise(await File.ReadAllTextAsync(path)) == generated,
            "openapi/svc-baggage-reclaim.json is out of date. Run UPDATE_OPENAPI=1 dotnet test --filter OpenApiExportTests and commit it.");
    }

    private static string SpecPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("*.sln").Length == 0)
        {
            dir = dir.Parent;
        }

        return Path.Combine(
            dir?.FullName ?? throw new InvalidOperationException("Solution directory not found."),
            "openapi",
            "svc-baggage-reclaim.json");
    }

    private static string Normalise(string json) =>
        JsonNode.Parse(json)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n";
}
