using MermaidMcpServer.Mcp;
using MermaidMcpServer.Options;
using MermaidMcpServer.Rendering;

namespace MermaidMcpServer.Tests;

public class MermaidToolsTests
{
    private static MermaidTools Build(bool enableServerRender)
    {
        var rendering = Microsoft.Extensions.Options.Options.Create(new RenderingOptions { EnableServerRender = enableServerRender });
        var server = Microsoft.Extensions.Options.Options.Create(new ServerOptions { PublicBaseUrl = "http://localhost:5000" });
        return new MermaidTools(rendering, server, new HeadlessRenderer(rendering));
    }

    [Test]
    public async Task RenderMermaid_code_returns_preview_url_and_source()
    {
        var result = await Build(false).RenderMermaid("flowchart TD\nA-->B", "code");
        await Assert.That(result).Contains("Preview: http://localhost:5000/?code=");
        await Assert.That(result).Contains("flowchart TD");
    }

    [Test]
    public async Task RenderMermaid_svg_when_disabled_reports_disabled()
    {
        var result = await Build(false).RenderMermaid("x", "svg");
        await Assert.That(result).Contains("disabled");
    }

    [Test]
    public async Task ValidateMermaid_when_disabled_reports_disabled()
    {
        var result = await Build(false).ValidateMermaid("x");
        await Assert.That(result).Contains("disabled");
    }
}
