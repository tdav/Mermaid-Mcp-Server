using System.Net;
using System.Text.RegularExpressions;
using MermaidMcpServer.Mcp;
using MermaidMcpServer.Mermaid;
using MermaidMcpServer.Options;
using MermaidMcpServer.Rendering;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RenderingOptions>(builder.Configuration.GetSection(RenderingOptions.SectionName));
builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection(ServerOptions.SectionName));
builder.Services.AddSingleton<HeadlessRenderer>();
builder.Services.AddMemoryCache();

// Streamable HTTP без сессий (по умолчанию Stateless = true) — ревизия протокола 2026-07-28.
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "mermaid", Version = "1.0.0" };
    })
    .WithHttpTransport()
    .WithTools<MermaidTools>()
    .WithTools<MermaidFileTools>()
    .WithTools<DiagramBuilderTools>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapMcp("/mcp");

// Файлы generate_mermaid_file: первый час — из кэша, дальше — с диска.
app.MapGet("/files/{name}", async (string name, HttpResponse response, IMemoryCache cache, IOptions<RenderingOptions> rendering) =>
{
    // Только имена вида {GUID}.png|svg: никаких "../" и чужих файлов из каталога.
    if (!Regex.IsMatch(name, "^[0-9A-F]{32}\\.(png|svg)$"))
        return Results.NotFound();

    if (!cache.TryGetValue(name, out byte[]? bytes))
    {
        var path = Path.Combine(rendering.Value.OutputDirectory, name);
        if (!File.Exists(path))
            return Results.NotFound();
        bytes = await File.ReadAllBytesAsync(path);
    }

    // Содержимое по GUID не меняется — браузер может не перезапрашивать файл.
    response.Headers.CacheControl = "public, max-age=31536000, immutable";
    var isPng = name.EndsWith(".png", StringComparison.Ordinal);
    if (!isPng)
        // SVG, открытый по прямой ссылке, — документ: запрещаем в нём скрипты.
        response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'";

    return Results.File(bytes!, isPng ? "image/png" : "image/svg+xml");
});

app.MapPost("/render", (HttpRequest request) =>
{
    var code = request.HasFormContentType ? request.Form["code"].ToString() : string.Empty;
    if (string.IsNullOrWhiteSpace(code))
        return Results.Content("<div class=\"diagram-error\">Empty diagram.</div>", "text/html");

    var encoded = WebUtility.HtmlEncode(code);
    return Results.Content($"<pre class=\"mermaid\">{encoded}</pre>", "text/html");
});

app.MapGet("/examples/{type}", (string type) =>
{
    var code = ExampleCatalog.Get(type);
    return code is null ? Results.NotFound() : Results.Text(code, "text/plain");
});

app.Run();

public partial class Program;
