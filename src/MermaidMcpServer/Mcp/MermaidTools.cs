using System.ComponentModel;
using MermaidMcpServer.Mermaid;
using MermaidMcpServer.Options;
using MermaidMcpServer.Rendering;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace MermaidMcpServer.Mcp;

[McpServerToolType]
public sealed class MermaidTools
{
    private const string DisabledMessage =
        "Server-side rendering is disabled. Set Rendering:EnableServerRender=true to enable svg/png/validate.";

    private readonly RenderingOptions rendering;
    private readonly ServerOptions server;
    private readonly HeadlessRenderer renderer;

    public MermaidTools(
        IOptions<RenderingOptions> rendering,
        IOptions<ServerOptions> server,
        HeadlessRenderer renderer)
    {
        this.rendering = rendering.Value;
        this.server = server.Value;
        this.renderer = renderer;
    }

    [McpServerTool(Name = "render_mermaid")]
    [Description("Render a Mermaid diagram. Supports ~18 Mermaid diagram types. format='code' returns the source plus a shareable preview URL; 'svg' returns SVG markup; 'png' returns a rendered image.")]
    public async Task<string> RenderMermaid(
        [Description("Mermaid diagram source code.")] string code,
        [Description("Output format: code | svg | png. Default: code.")] string format = "code")
    {
        if (string.Equals(format, "code", StringComparison.OrdinalIgnoreCase))
        {
            var url = MermaidPreview.BuildPreviewUrl(this.server.PublicBaseUrl, code);
            return $"Preview: {url}\n\n{code}";
        }

        if (!this.rendering.EnableServerRender)
            return DisabledMessage;

        return format.ToLowerInvariant() switch
        {
            "svg" => await this.renderer.RenderSvgAsync(code),
            "png" => Convert.ToBase64String(await this.renderer.RenderPngAsync(code)),
            _ => "Unknown format. Use code | svg | png.",
        };
    }

    [McpServerTool(Name = "validate_mermaid")]
    [Description("Validate Mermaid diagram syntax. Supports ~18 Mermaid diagram types. Requires server-side rendering to be enabled.")]
    public async Task<string> ValidateMermaid(
        [Description("Mermaid diagram source code.")] string code)
    {
        if (!this.rendering.EnableServerRender)
            return DisabledMessage;

        var (ok, error) = await this.renderer.ValidateAsync(code);
        return ok ? "Valid." : $"Invalid: {error}";
    }
}
