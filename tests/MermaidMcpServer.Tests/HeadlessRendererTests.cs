using MermaidMcpServer.Options;
using MermaidMcpServer.Rendering;
using Microsoft.Extensions.Options;

namespace MermaidMcpServer.Tests;

public class HeadlessRendererTests
{
    [Test]
    public async Task IsEnabled_reflects_options()
    {
        // Fully qualified: "Options" here is ambiguous between the Microsoft.Extensions.Options.Options
        // factory class and the sibling MermaidMcpServer.Options namespace.
        var on = new HeadlessRenderer(Microsoft.Extensions.Options.Options.Create(new RenderingOptions { EnableServerRender = true }));
        var off = new HeadlessRenderer(Microsoft.Extensions.Options.Options.Create(new RenderingOptions { EnableServerRender = false }));
        await Assert.That(on.IsEnabled).IsTrue();
        await Assert.That(off.IsEnabled).IsFalse();
    }

    [Test]
    public async Task Constructor_does_not_throw_or_launch()
    {
        // Must be safe to construct without Chromium present.
        var renderer = new HeadlessRenderer(Microsoft.Extensions.Options.Options.Create(new RenderingOptions { EnableServerRender = false }));
        await renderer.DisposeAsync();
        await Assert.That(renderer.IsEnabled).IsFalse();
    }
}
