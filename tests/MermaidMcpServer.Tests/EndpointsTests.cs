using Microsoft.AspNetCore.Mvc.Testing;

namespace MermaidMcpServer.Tests;

public class EndpointsTests
{
    [Test]
    public async Task Post_render_returns_mermaid_fragment_html_encoded()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("code", "flowchart TD\nA-->B"),
        });
        var response = await client.PostAsync("/render", form);
        var html = await response.Content.ReadAsStringAsync();

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(html).Contains("class=\"mermaid\"");
        await Assert.That(html).Contains("A--&gt;B");
    }

    [Test]
    public async Task Get_examples_flowchart_returns_code()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/examples/flowchart");
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(body).Contains("-->");
    }

    [Test]
    public async Task Get_examples_unknown_returns_404()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/examples/does-not-exist");
        await Assert.That((int)response.StatusCode).IsEqualTo(404);
    }
}
