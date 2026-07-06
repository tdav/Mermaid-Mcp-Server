namespace MermaidMcpServer.Options;

public sealed class ServerOptions
{
    public const string SectionName = "Server";

    // Base URL used to build shareable preview links returned by the render_mermaid tool.
    public string PublicBaseUrl { get; set; } = "http://localhost:5000";
}
