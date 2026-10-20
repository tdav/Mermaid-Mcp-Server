using System.ComponentModel;
using System.Text;
using MermaidMcpServer.Options;
using MermaidMcpServer.Rendering;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace MermaidMcpServer.Mcp;

[McpServerToolType]
public sealed class MermaidFileTools
{
    private readonly RenderingOptions rendering;
    private readonly ServerOptions server;
    private readonly HeadlessRenderer renderer;
    private readonly IMemoryCache cache;

    public MermaidFileTools(
        IOptions<RenderingOptions> rendering,
        IOptions<ServerOptions> server,
        HeadlessRenderer renderer,
        IMemoryCache cache)
    {
        this.rendering = rendering.Value;
        this.server = server.Value;
        this.renderer = renderer;
        this.cache = cache;
    }

    [McpServerTool(Name = "generate_mermaid_file")]
    [Description("Render a Mermaid diagram to a PNG or SVG file on the server and return a permanent URL to it. Store the URL and embed it in documents (Markdown image, <img src>); the browser fetches the file from that URL.")]
    public async Task<string> GenerateMermaidFile(
        [Description("Mermaid diagram source code.")] string code,
        [Description("File format: png | svg. Default: png.")] string format = "png")
    {
        if (!this.rendering.EnableServerRender)
            return MermaidTools.DisabledMessage;

        var extension = format.ToLowerInvariant();
        if (extension is not ("png" or "svg"))
            return "Unknown format. Use png | svg.";

        byte[] bytes;
        try
        {
            bytes = extension == "png"
                ? await this.renderer.RenderPngAsync(code)
                : Encoding.UTF8.GetBytes(await this.renderer.RenderSvgAsync(code));
        }
        catch (Exception e) when (e is not McpException)
        {
            throw new McpException($"Render to '{extension}' failed: {e.GetType().Name}: {e.Message}", e);
        }

        var fileName = $"{Guid.NewGuid().ToString("N").ToUpperInvariant()}.{extension}";
        Directory.CreateDirectory(this.rendering.OutputDirectory);
        await File.WriteAllBytesAsync(Path.Combine(this.rendering.OutputDirectory, fileName), bytes);

        // ponytail: кэш без SizeLimit — при тысячах генераций в час память растёт; тогда задать SizeLimit и Size записи.
        this.cache.Set(fileName, bytes, TimeSpan.FromHours(1));

        return $"{this.server.PublicBaseUrl.TrimEnd('/')}/files/{fileName}";
    }
}
