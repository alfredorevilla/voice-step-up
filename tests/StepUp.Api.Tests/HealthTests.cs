using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace StepUp.Api.Tests;

public class HealthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Healthz_returns_200_with_status_ok()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/healthz", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"status\":\"ok\"", body);
    }
}
