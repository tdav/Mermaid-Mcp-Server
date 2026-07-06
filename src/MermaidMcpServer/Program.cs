using System.Net;
using MermaidMcpServer.Mermaid;
using MermaidMcpServer.Options;
using MermaidMcpServer.Rendering;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RenderingOptions>(builder.Configuration.GetSection(RenderingOptions.SectionName));
builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection(ServerOptions.SectionName));
builder.Services.AddSingleton<HeadlessRenderer>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

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
