using Microsoft.AspNetCore.Mvc.Testing;

namespace MermaidMcpServer.Tests;

public class SseEndpointTests
{
    [Test]
    public async Task Get_sse_returns_event_stream()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/sse");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("text/event-stream");
    }
}
