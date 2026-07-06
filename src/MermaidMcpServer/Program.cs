using System.Net;
using MermaidMcpServer.Mcp;
using MermaidMcpServer.Mermaid;
using MermaidMcpServer.Options;
using MermaidMcpServer.Rendering;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RenderingOptions>(builder.Configuration.GetSection(RenderingOptions.SectionName));
builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection(ServerOptions.SectionName));
builder.Services.AddSingleton<HeadlessRenderer>();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "mermaid", Version = "1.0.0" };
    })
    .WithHttpTransport(transport =>
    {
        // Legacy SSE requires stateful mode.
        transport.Stateless = false;
#pragma warning disable MCP9004 // EnableLegacySse is obsolete
        transport.EnableLegacySse = true;
#pragma warning restore MCP9004
    })
    .WithTools<MermaidTools>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Explicit UseRouting placed after static files: without it, WebApplication
// auto-inserts routing at the top of the pipeline, so "/" (mapped by MapMcp)
// would match before static files get a chance to serve wwwroot/index.html.
app.UseRouting();

app.MapMcp();

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
