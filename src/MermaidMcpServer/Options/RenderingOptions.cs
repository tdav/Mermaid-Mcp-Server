namespace MermaidMcpServer.Options;

public sealed class RenderingOptions
{
    public const string SectionName = "Rendering";

    // Opt-in gate for svg/png/validate. Rendering is pure-managed (Mermaider + Svg.Skia) —
    // no Chromium download or browser process. Safe to default to true if you want it always on.
    public bool EnableServerRender { get; set; }

    // Каталог для файлов generate_mermaid_file ({GUID}.png|svg), отдаются через GET /files/{name}.
    // На сервере должен лежать вне каталога приложения: деплой подменяет его целиком.
    public string OutputDirectory { get; set; } = ".";
}
