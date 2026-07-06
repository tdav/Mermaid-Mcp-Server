using Microsoft.AspNetCore.Mvc.Testing;

namespace MermaidMcpServer.Tests;

public class ShellTests
{
    [Test]
    public async Task Root_serves_shell_with_grid_and_canvas()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");

        await Assert.That(html).Contains("grid-cols-12");
        await Assert.That(html).Contains("col-span-2");
        await Assert.That(html).Contains("col-span-10");
        await Assert.That(html).Contains("id=\"canvas\"");
    }
}
