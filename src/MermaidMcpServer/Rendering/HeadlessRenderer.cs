using MermaidMcpServer.Options;
using Microsoft.Extensions.Options;
using PuppeteerSharp;

namespace MermaidMcpServer.Rendering;

// Owns the single headless Chromium. Lazy: nothing is launched until the first render/validate call.
public sealed class HeadlessRenderer : IAsyncDisposable
{
    private readonly RenderingOptions options;
    private readonly SemaphoreSlim gate = new(1, 1);
    private IBrowser? browser;
    private string? mermaidJs;

    public HeadlessRenderer(IOptions<RenderingOptions> options) => this.options = options.Value;

    public bool IsEnabled => this.options.EnableServerRender;

    public async Task<string> RenderSvgAsync(string code)
    {
        await using var page = await this.NewPageAsync();
        return await page.EvaluateFunctionAsync<string>(
            "async (code) => { const { svg } = await mermaid.render('g', code); return svg; }", code);
    }

    public async Task<byte[]> RenderPngAsync(string code)
    {
        await using var page = await this.NewPageAsync();
        await page.EvaluateFunctionAsync(
            "async (code) => { const { svg } = await mermaid.render('g', code); document.getElementById('c').innerHTML = svg; }", code);
        var element = await page.QuerySelectorAsync("#c svg");
        return await element.ScreenshotDataAsync();
    }

    public async Task<(bool ok, string? error)> ValidateAsync(string code)
    {
        await using var page = await this.NewPageAsync();
        var result = await page.EvaluateFunctionAsync<ValidationResult>(
            "async (code) => { try { await mermaid.parse(code); return { ok: true, error: null }; } catch (e) { return { ok: false, error: String(e && e.message ? e.message : e) }; } }", code);
        return (result.Ok, result.Error);
    }

    private async Task<IPage> NewPageAsync()
    {
        await this.EnsureBrowserAsync();
        var page = await this.browser!.NewPageAsync();
        await page.SetContentAsync($"<!doctype html><html><body><div id='c'></div><script>{this.mermaidJs}</script><script>mermaid.initialize({{ startOnLoad: false }});</script></body></html>");
        return page;
    }

    private async Task EnsureBrowserAsync()
    {
        if (this.browser is not null) return;
        await this.gate.WaitAsync();
        try
        {
            if (this.browser is not null) return;
            this.mermaidJs = await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "wwwroot", "lib", "mermaid.min.js"));
            await new BrowserFetcher().DownloadAsync();
            this.browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true });
        }
        finally
        {
            this.gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (this.browser is not null) await this.browser.DisposeAsync();
        this.gate.Dispose();
    }

    private sealed class ValidationResult
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
    }
}
