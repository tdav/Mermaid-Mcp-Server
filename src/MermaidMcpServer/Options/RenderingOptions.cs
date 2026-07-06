namespace MermaidMcpServer.Options;

public sealed class RenderingOptions
{
    public const string SectionName = "Rendering";

    // Opt-in: enabling this launches/downloads a headless Chromium on first svg/png/validate call.
    public bool EnableServerRender { get; set; }
}
