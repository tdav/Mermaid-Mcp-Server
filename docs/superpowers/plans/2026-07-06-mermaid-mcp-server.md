# MermaidMcpServer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a single ASP.NET Core (.NET 10) app that renders Mermaid diagrams in a two-pane web UI and exposes an MCP server over SSE with tools covering ~18 Mermaid diagram types.

**Architecture:** One web app serves both a static two-pane HTML shell (paste code → `mermaid.js` renders client-side, wired with HTMX) and an MCP endpoint (`MapMcp` with legacy SSE). `FoggyBalrog.MermaidDotNet` generates Mermaid text; visual rendering is done by `mermaid.js` (browser) and, optionally, by a lazy opt-in `HeadlessRenderer` (PuppeteerSharp) for server-side SVG/PNG used by MCP.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, FoggyBalrog.MermaidDotNet, ModelContextProtocol.AspNetCore, PuppeteerSharp (opt-in), HTMX + Tailwind (Play CDN), mermaid.js (vendored), TUnit (tests).

## Global Constraints

- Target framework: `net10.0`. Web project `Microsoft.NET.Sdk.Web`; test project `Microsoft.NET.Sdk` with `OutputType=Exe`, `IsPackable=false`, package `TUnit` (no `Microsoft.NET.Test.Sdk`).
- Private fields: **no** `_` prefix; access via `this.` (e.g. `private readonly RenderingOptions options;` → `this.options`).
- Code identifiers, comments, commit messages: English. Docs (README): Russian.
- MCP: legacy SSE enabled → `options.Stateless = false; options.EnableLegacySse = true;`. SSE endpoints are `/sse` (GET stream) and `/message` (POST). Server metadata/tool descriptions state support for **~18 Mermaid diagram types**.
- Design system: import tokens from `DESIGN.json` / `DESIGN.md` ("The Precision Instrument"). Flat elevation, hairline 1px borders, Ember Orange (`#FF6B2C`) **only** for interactive elements, Inter + JetBrains Mono, dark + light themes via CSS variables. **No** neumorphism, glassmorphism, decorative shadows, `background-clip:text` gradients. WCAG AA; honor `prefers-reduced-motion`; status never by color alone.
- Web UI grid: Tailwind `grid-cols-12`, left `<textarea>` `col-span-2`, right canvas `col-span-10`.
- Server-side rendering (PuppeteerSharp/Chromium) is **opt-in** via `Rendering:EnableServerRender` (default `false`). When disabled, svg/png/validate MCP tools return a clear "disabled" message and never launch Chromium.
- Note: `FoggyBalrog.MermaidDotNet` README marks itself "still under development, not ready for production" — pin the resolved version after `dotnet add`.
- Testing: full TDD for all deterministic logic (pure helpers, endpoints via `WebApplicationFactory`, tool guards, SSE endpoint, shell markup, FoggyBalrog code generation). The actual Chromium pixel-render path is verified **manually** (documented), not in the red-green loop.

---

## File Structure

```
Mermaid-Mcp-Server/
  MermaidMcpServer.sln
  src/MermaidMcpServer/
    MermaidMcpServer.csproj
    Program.cs                     host, DI, /render, /examples, static files, MapMcp (SSE); ends with `public partial class Program;`
    Options/RenderingOptions.cs    EnableServerRender
    Options/ServerOptions.cs       PublicBaseUrl
    Mermaid/MermaidPreview.cs      Base64Url encode/decode + preview URL (pure)
    Mermaid/ExampleCatalog.cs      per-type example generators (FoggyBalrog)
    Rendering/HeadlessRenderer.cs  PuppeteerSharp, lazy, opt-in: RenderSvgAsync/RenderPngAsync/ValidateAsync
    Mcp/MermaidTools.cs            render_mermaid, validate_mermaid
    Mcp/DiagramBuilderTools.cs     create_flowchart (+ more in follow-up plans)
    wwwroot/index.html             two-pane shell
    wwwroot/css/tokens.css         design-system CSS variables (from DESIGN.json)
    wwwroot/js/app.js              theme toggle, mermaid init, HTMX hooks, export, examples
    wwwroot/lib/htmx.min.js        vendored
    wwwroot/lib/mermaid.min.js     vendored
    appsettings.json
  tests/MermaidMcpServer.Tests/
    MermaidMcpServer.Tests.csproj
    MermaidPreviewTests.cs
    ExampleCatalogTests.cs
    MermaidToolsTests.cs
    EndpointsTests.cs
    SseEndpointTests.cs
    ShellTests.cs
    DiagramBuilderToolsTests.cs
  README.md
```

Each source file has one responsibility. Program.cs grows incrementally as tasks add endpoints/DI.

---

### Task 1: Solution, projects, TDD loop

**Files:**
- Create: `MermaidMcpServer.sln`, `src/MermaidMcpServer/MermaidMcpServer.csproj`, `src/MermaidMcpServer/Program.cs`
- Create: `tests/MermaidMcpServer.Tests/MermaidMcpServer.Tests.csproj`, `tests/MermaidMcpServer.Tests/SmokeTests.cs`

**Interfaces:**
- Produces: runnable web app with `public partial class Program;`; test project running under TUnit via `dotnet test`.

- [ ] **Step 1: Init git and solution scaffold**

```bash
cd "C:/Works_Tools/Mermaid-Mcp-Server"
git init
dotnet new sln -n MermaidMcpServer
dotnet new web -n MermaidMcpServer -o src/MermaidMcpServer
dotnet sln add src/MermaidMcpServer/MermaidMcpServer.csproj
```

- [ ] **Step 2: Create the TUnit test project**

```bash
dotnet new classlib -n MermaidMcpServer.Tests -o tests/MermaidMcpServer.Tests
dotnet sln add tests/MermaidMcpServer.Tests/MermaidMcpServer.Tests.csproj
dotnet add tests/MermaidMcpServer.Tests reference src/MermaidMcpServer/MermaidMcpServer.csproj
dotnet add tests/MermaidMcpServer.Tests package TUnit
dotnet add tests/MermaidMcpServer.Tests package Microsoft.AspNetCore.Mvc.Testing
```

Replace `tests/MermaidMcpServer.Tests/MermaidMcpServer.Tests.csproj` content with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="TUnit" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MermaidMcpServer\MermaidMcpServer.csproj" />
  </ItemGroup>
</Project>
```

Delete the default `Class1.cs` from the test project.

- [ ] **Step 3: Make `Program` referenceable for integration tests**

Ensure `src/MermaidMcpServer/Program.cs` ends with (after the `app.Run();` line):

```csharp
public partial class Program;
```

- [ ] **Step 4: Write the smoke test**

`tests/MermaidMcpServer.Tests/SmokeTests.cs`:

```csharp
namespace MermaidMcpServer.Tests;

public class SmokeTests
{
    [Test]
    public async Task Test_harness_runs()
    {
        await Assert.That(1 + 1).IsEqualTo(2);
    }
}
```

- [ ] **Step 5: Run tests — expect PASS**

Run: `dotnet test`
Expected: 1 test passed.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "chore: scaffold solution, web app, and TUnit test project"
```

---

### Task 2: MermaidPreview (Base64Url + preview URL)

**Files:**
- Create: `src/MermaidMcpServer/Mermaid/MermaidPreview.cs`
- Test: `tests/MermaidMcpServer.Tests/MermaidPreviewTests.cs`

**Interfaces:**
- Produces: `static class MermaidPreview` with `string ToBase64Url(string)`, `string FromBase64Url(string)`, `string BuildPreviewUrl(string baseUrl, string code)`.

- [ ] **Step 1: Write the failing tests**

`tests/MermaidMcpServer.Tests/MermaidPreviewTests.cs`:

```csharp
using MermaidMcpServer.Mermaid;

namespace MermaidMcpServer.Tests;

public class MermaidPreviewTests
{
    [Test]
    [Arguments("flowchart TD\n  A[Start] --> B{OK?}")]
    [Arguments("sequenceDiagram\n  Alice->>Bob: Hi")]
    public async Task Base64Url_roundtrips(string code)
    {
        var encoded = MermaidPreview.ToBase64Url(code);
        await Assert.That(MermaidPreview.FromBase64Url(encoded)).IsEqualTo(code);
    }

    [Test]
    public async Task BuildPreviewUrl_starts_with_base_and_code_query()
    {
        var url = MermaidPreview.BuildPreviewUrl("http://localhost:5000/", "flowchart TD\nA-->B");
        await Assert.That(url).StartsWith("http://localhost:5000/?code=");
    }
}
```

- [ ] **Step 2: Run — expect FAIL (type not found)**

Run: `dotnet test --filter "MermaidPreviewTests"`
Expected: compile/FAIL — `MermaidPreview` does not exist.

- [ ] **Step 3: Implement**

`src/MermaidMcpServer/Mermaid/MermaidPreview.cs`:

```csharp
using System.Text;

namespace MermaidMcpServer.Mermaid;

// Stateless: the diagram code is carried in the preview URL itself, so no server storage is needed.
public static class MermaidPreview
{
    public static string ToBase64Url(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s };
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    public static string BuildPreviewUrl(string baseUrl, string code)
        => $"{baseUrl.TrimEnd('/')}/?code={ToBase64Url(code)}";
}
```

- [ ] **Step 4: Run — expect PASS**

Run: `dotnet test --filter "MermaidPreviewTests"`
Expected: 3 tests passed.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add MermaidPreview base64url preview URL helper"
```

---

### Task 3: ExampleCatalog (FoggyBalrog code generation)

**Files:**
- Create: `src/MermaidMcpServer/Mermaid/ExampleCatalog.cs`
- Modify: `src/MermaidMcpServer/MermaidMcpServer.csproj` (add FoggyBalrog package)
- Test: `tests/MermaidMcpServer.Tests/ExampleCatalogTests.cs`

**Interfaces:**
- Produces: `static class ExampleCatalog` with `IReadOnlyCollection<string> Types` and `string? Get(string type)`.

- [ ] **Step 1: Add the FoggyBalrog package**

```bash
dotnet add src/MermaidMcpServer package FoggyBalrog.MermaidDotNet
```

- [ ] **Step 2: Write the failing tests**

`tests/MermaidMcpServer.Tests/ExampleCatalogTests.cs`:

```csharp
using MermaidMcpServer.Mermaid;

namespace MermaidMcpServer.Tests;

public class ExampleCatalogTests
{
    [Test]
    public async Task Get_flowchart_returns_mermaid_with_link()
    {
        var code = ExampleCatalog.Get("flowchart");
        await Assert.That(code).IsNotNull();
        await Assert.That(code!).Contains("-->");
    }

    [Test]
    public async Task Get_unknown_type_returns_null()
    {
        await Assert.That(ExampleCatalog.Get("does-not-exist")).IsNull();
    }

    [Test]
    public async Task Types_contains_flowchart()
    {
        await Assert.That(ExampleCatalog.Types).Contains("flowchart");
    }
}
```

- [ ] **Step 3: Run — expect FAIL**

Run: `dotnet test --filter "ExampleCatalogTests"`
Expected: compile/FAIL — `ExampleCatalog` does not exist.

- [ ] **Step 4: Implement**

`src/MermaidMcpServer/Mermaid/ExampleCatalog.cs`:

```csharp
using FoggyBalrog.MermaidDotNet;

namespace MermaidMcpServer.Mermaid;

// Sample diagram code per type, generated with FoggyBalrog. Extended incrementally.
public static class ExampleCatalog
{
    private static readonly Dictionary<string, Func<string>> Generators =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["flowchart"] = () => Mermaid
                .Flowchart()
                .AddNode("Start", out var start)
                .AddNode("Process", out var process)
                .AddNode("End", out var end)
                .AddLink(start, process)
                .AddLink(process, end)
                .Build(),
        };

    public static IReadOnlyCollection<string> Types => Generators.Keys;

    public static string? Get(string type)
        => Generators.TryGetValue(type, out var generate) ? generate() : null;
}
```

- [ ] **Step 5: Run — expect PASS**

Run: `dotnet test --filter "ExampleCatalogTests"`
Expected: 3 tests passed.

If the flowchart assertion on `"-->"` fails, inspect the actual output (`Console.WriteLine(ExampleCatalog.Get("flowchart"))`) and adjust the asserted substring to a token the builder actually emits (e.g. a node label). Do not weaken the test to always-true.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: add ExampleCatalog with FoggyBalrog flowchart sample"
```

---

### Task 4: Options + HeadlessRenderer (lazy, opt-in)

**Files:**
- Create: `src/MermaidMcpServer/Options/RenderingOptions.cs`, `src/MermaidMcpServer/Options/ServerOptions.cs`
- Create: `src/MermaidMcpServer/Rendering/HeadlessRenderer.cs`
- Modify: `src/MermaidMcpServer/MermaidMcpServer.csproj` (add PuppeteerSharp)
- Test: `tests/MermaidMcpServer.Tests/HeadlessRendererTests.cs`

**Interfaces:**
- Produces: `RenderingOptions { bool EnableServerRender }`, `ServerOptions { string PublicBaseUrl }`.
- Produces: `class HeadlessRenderer(IOptions<RenderingOptions>)` with `bool IsEnabled`, `Task<string> RenderSvgAsync(string)`, `Task<byte[]> RenderPngAsync(string)`, `Task<(bool ok, string? error)> ValidateAsync(string)`, `ValueTask DisposeAsync()`. Constructor does **not** launch Chromium.

- [ ] **Step 1: Add PuppeteerSharp**

```bash
dotnet add src/MermaidMcpServer package PuppeteerSharp
```

- [ ] **Step 2: Write the failing tests (deterministic parts only)**

`tests/MermaidMcpServer.Tests/HeadlessRendererTests.cs`:

```csharp
using MermaidMcpServer.Options;
using MermaidMcpServer.Rendering;
using Microsoft.Extensions.Options;

namespace MermaidMcpServer.Tests;

public class HeadlessRendererTests
{
    [Test]
    public async Task IsEnabled_reflects_options()
    {
        var on = new HeadlessRenderer(Options.Create(new RenderingOptions { EnableServerRender = true }));
        var off = new HeadlessRenderer(Options.Create(new RenderingOptions { EnableServerRender = false }));
        await Assert.That(on.IsEnabled).IsTrue();
        await Assert.That(off.IsEnabled).IsFalse();
    }

    [Test]
    public async Task Constructor_does_not_throw_or_launch()
    {
        // Must be safe to construct without Chromium present.
        var renderer = new HeadlessRenderer(Options.Create(new RenderingOptions { EnableServerRender = false }));
        await renderer.DisposeAsync();
        await Assert.That(renderer.IsEnabled).IsFalse();
    }
}
```

- [ ] **Step 3: Run — expect FAIL**

Run: `dotnet test --filter "HeadlessRendererTests"`
Expected: compile/FAIL — types do not exist.

- [ ] **Step 4: Implement options**

`src/MermaidMcpServer/Options/RenderingOptions.cs`:

```csharp
namespace MermaidMcpServer.Options;

public sealed class RenderingOptions
{
    public const string SectionName = "Rendering";

    // Opt-in: enabling this launches/downloads a headless Chromium on first svg/png/validate call.
    public bool EnableServerRender { get; set; }
}
```

`src/MermaidMcpServer/Options/ServerOptions.cs`:

```csharp
namespace MermaidMcpServer.Options;

public sealed class ServerOptions
{
    public const string SectionName = "Server";

    // Base URL used to build shareable preview links returned by the render_mermaid tool.
    public string PublicBaseUrl { get; set; } = "http://localhost:5000";
}
```

- [ ] **Step 5: Implement HeadlessRenderer**

`src/MermaidMcpServer/Rendering/HeadlessRenderer.cs`:

```csharp
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
```

- [ ] **Step 6: Run — expect PASS**

Run: `dotnet test --filter "HeadlessRendererTests"`
Expected: 2 tests passed (no Chromium download — constructor is lazy).

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: add rendering options and lazy opt-in HeadlessRenderer"
```

---

### Task 5: MCP tools — render_mermaid & validate_mermaid

**Files:**
- Create: `src/MermaidMcpServer/Mcp/MermaidTools.cs`
- Modify: `src/MermaidMcpServer/MermaidMcpServer.csproj` (add MCP package)
- Test: `tests/MermaidMcpServer.Tests/MermaidToolsTests.cs`

**Interfaces:**
- Consumes: `MermaidPreview`, `HeadlessRenderer`, `RenderingOptions`, `ServerOptions`.
- Produces: `[McpServerToolType] class MermaidTools` with `Task<string> RenderMermaid(string code, string format = "code")` and `Task<string> ValidateMermaid(string code)`.

- [ ] **Step 1: Add the MCP package**

```bash
dotnet add src/MermaidMcpServer package ModelContextProtocol.AspNetCore --prerelease
```

- [ ] **Step 2: Write the failing tests**

`tests/MermaidMcpServer.Tests/MermaidToolsTests.cs`:

```csharp
using MermaidMcpServer.Mcp;
using MermaidMcpServer.Options;
using MermaidMcpServer.Rendering;
using Microsoft.Extensions.Options;

namespace MermaidMcpServer.Tests;

public class MermaidToolsTests
{
    private static MermaidTools Build(bool enableServerRender)
    {
        var rendering = Options.Create(new RenderingOptions { EnableServerRender = enableServerRender });
        var server = Options.Create(new ServerOptions { PublicBaseUrl = "http://localhost:5000" });
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
```

- [ ] **Step 3: Run — expect FAIL**

Run: `dotnet test --filter "MermaidToolsTests"`
Expected: compile/FAIL — `MermaidTools` does not exist.

- [ ] **Step 4: Implement**

`src/MermaidMcpServer/Mcp/MermaidTools.cs`:

```csharp
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

    private const string DisabledMessage =
        "Server-side rendering is disabled. Set Rendering:EnableServerRender=true to enable svg/png/validate.";

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
```

> Note: `png` returns base64 text here for a self-contained tool. Returning MCP image content blocks (so the model "sees" the diagram) is a follow-up refinement once the Chromium path is verified manually.

- [ ] **Step 5: Run — expect PASS**

Run: `dotnet test --filter "MermaidToolsTests"`
Expected: 3 tests passed.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: add render_mermaid and validate_mermaid MCP tools"
```

---

### Task 6: Web endpoints — /render and /examples

**Files:**
- Modify: `src/MermaidMcpServer/Program.cs`
- Test: `tests/MermaidMcpServer.Tests/EndpointsTests.cs`

**Interfaces:**
- Consumes: `ExampleCatalog`.
- Produces: `POST /render` (form `code` → HTML fragment), `GET /examples/{type}` (→ Mermaid code / 404).

- [ ] **Step 1: Write the failing tests**

`tests/MermaidMcpServer.Tests/EndpointsTests.cs`:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;

namespace MermaidMcpServer.Tests;

public class EndpointsTests
{
    [Test]
    public async Task Post_render_returns_mermaid_fragment_html_encoded()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("code", "flowchart TD\nA-->B"),
        });
        var response = await client.PostAsync("/render", form);
        var html = await response.Content.ReadAsStringAsync();

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(html).Contains("class=\"mermaid\"");
        await Assert.That(html).Contains("A--&gt;B");
    }

    [Test]
    public async Task Get_examples_flowchart_returns_code()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/examples/flowchart");
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(body).Contains("-->");
    }

    [Test]
    public async Task Get_examples_unknown_returns_404()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/examples/does-not-exist");
        await Assert.That((int)response.StatusCode).IsEqualTo(404);
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

Run: `dotnet test --filter "EndpointsTests"`
Expected: FAIL — routes return 404 / no fragment.

- [ ] **Step 3: Implement — replace Program.cs**

`src/MermaidMcpServer/Program.cs` (full file at this stage; MCP wiring added in Task 7):

```csharp
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
```

- [ ] **Step 4: Run — expect PASS**

Run: `dotnet test --filter "EndpointsTests"`
Expected: 3 tests passed.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add /render and /examples web endpoints"
```

---

### Task 7: MCP server hosting over SSE

**Files:**
- Modify: `src/MermaidMcpServer/Program.cs`
- Modify: `src/MermaidMcpServer/appsettings.json`
- Test: `tests/MermaidMcpServer.Tests/SseEndpointTests.cs`

**Interfaces:**
- Consumes: `MermaidTools`.
- Produces: legacy SSE endpoint at `GET /sse` (content-type `text/event-stream`) and `POST /message`; Streamable HTTP at root.

- [ ] **Step 1: Write the failing test**

`tests/MermaidMcpServer.Tests/SseEndpointTests.cs`:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;

namespace MermaidMcpServer.Tests;

public class SseEndpointTests
{
    [Test]
    public async Task Get_sse_returns_event_stream()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/sse");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("text/event-stream");
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

Run: `dotnet test --filter "SseEndpointTests"`
Expected: FAIL — `/sse` returns 404.

- [ ] **Step 3: Implement — add MCP wiring to Program.cs**

In `src/MermaidMcpServer/Program.cs`, add the `using` and MCP registration before `var app = builder.Build();`:

```csharp
using MermaidMcpServer.Mcp;
```

```csharp
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
```

After `app.UseStaticFiles();` (and before `app.Run();`) add:

```csharp
app.MapMcp();
```

- [ ] **Step 4: Add config defaults**

`src/MermaidMcpServer/appsettings.json` — add sections:

```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*",
  "Rendering": { "EnableServerRender": false },
  "Server": { "PublicBaseUrl": "http://localhost:5000" }
}
```

- [ ] **Step 5: Run — expect PASS**

Run: `dotnet test --filter "SseEndpointTests"`
Expected: 1 test passed.

- [ ] **Step 6: Full suite green**

Run: `dotnet test`
Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: host MCP server over legacy SSE at /sse"
```

---

### Task 8: Two-pane web UI with the design system

**Files:**
- Create: `src/MermaidMcpServer/wwwroot/index.html`, `wwwroot/css/tokens.css`, `wwwroot/js/app.js`
- Create (vendored): `wwwroot/lib/htmx.min.js`, `wwwroot/lib/mermaid.min.js`
- Test: `tests/MermaidMcpServer.Tests/ShellTests.cs`

**Interfaces:**
- Consumes: `POST /render`, `GET /examples/{type}`.
- Produces: served shell at `GET /` containing the 12-col grid, `#canvas`, textarea, theme toggle.

- [ ] **Step 1: Vendor the libraries**

Download into `wwwroot/lib/`:

```bash
curl -L https://unpkg.com/htmx.org/dist/htmx.min.js -o src/MermaidMcpServer/wwwroot/lib/htmx.min.js
curl -L https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js -o src/MermaidMcpServer/wwwroot/lib/mermaid.min.js
```

Add to `src/MermaidMcpServer/MermaidMcpServer.csproj` so vendored assets ship next to the app (needed by `HeadlessRenderer`):

```xml
<ItemGroup>
  <Content Include="wwwroot\lib\mermaid.min.js" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 2: Write the failing test**

`tests/MermaidMcpServer.Tests/ShellTests.cs`:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;

namespace MermaidMcpServer.Tests;

public class ShellTests
{
    [Test]
    public async Task Root_serves_shell_with_grid_and_canvas()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");

        await Assert.That(html).Contains("grid-cols-12");
        await Assert.That(html).Contains("col-span-2");
        await Assert.That(html).Contains("col-span-10");
        await Assert.That(html).Contains("id=\"canvas\"");
    }
}
```

- [ ] **Step 3: Run — expect FAIL**

Run: `dotnet test --filter "ShellTests"`
Expected: FAIL — `/` returns 404 (no index.html yet).

- [ ] **Step 4: Implement tokens.css (from DESIGN.json)**

`src/MermaidMcpServer/wwwroot/css/tokens.css`:

```css
:root {
  --ember-orange: #FF6B2C; --ember-orange-light: #FF8C42;
  --bg: #2D2D35; --panel: #363640; --inset: #1E1E26; --surface: #3A3A45;
  --border: rgba(255,255,255,0.08); --border-strong: rgba(255,255,255,0.14);
  --text-primary: #FFFFFF; --text-secondary: #8A8A9A; --text-muted: #7D7D8A;
  --sem-danger: #FF4D4D; --sem-success: #4ADE80; --sem-warning: #FFB02E; --sem-info: #4DA6FF;
  --font-ui: Inter, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
  --font-mono: 'JetBrains Mono', 'Fira Code', 'Cascadia Code', monospace;
}
:root[data-theme="light"] {
  --bg: #F0F0F5; --panel: #FFFFFF; --inset: #E4E4EC; --surface: #FFFFFF;
  --border: rgba(26,26,46,0.10); --border-strong: rgba(26,26,46,0.18);
  --text-primary: #1A1A2E;
  --sem-danger: #DC2626; --sem-success: #16A34A; --sem-warning: #D97706; --sem-info: #2563EB;
}
body { background: var(--bg); color: var(--text-primary); font-family: var(--font-ui); margin: 0; }
textarea { background: var(--inset); color: var(--text-primary); border: 1px solid var(--border);
  border-radius: 8px; font-family: var(--font-mono); padding: 12px; resize: none; }
textarea:focus { outline: none; border-color: var(--ember-orange); }
.panel { background: var(--panel); border: 1px solid var(--border); border-radius: 16px; }
.btn-accent { background: linear-gradient(135deg, var(--ember-orange), var(--ember-orange-light));
  color: #fff; border: none; border-radius: 8px; padding: 8px 16px; cursor: pointer; }
.diagram-error { color: var(--sem-danger); font-family: var(--font-mono); padding: 16px; }
@media (prefers-reduced-motion: reduce) { * { transition: none !important; animation: none !important; } }
```

- [ ] **Step 5: Implement index.html**

`src/MermaidMcpServer/wwwroot/index.html`:

```html
<!doctype html>
<html lang="ru" data-theme="dark">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Mermaid Renderer</title>
  <link rel="stylesheet" href="/css/tokens.css" />
  <script src="https://cdn.tailwindcss.com"></script>
  <script>
    tailwind.config = { theme: { extend: { colors: {
      ember: '#FF6B2C', panel: '#363640', inset: '#1E1E26'
    } } } };
  </script>
  <script src="/lib/htmx.min.js"></script>
  <script src="/lib/mermaid.min.js"></script>
</head>
<body>
  <div class="grid grid-cols-12 gap-4 p-4" style="height:100vh;box-sizing:border-box;">
    <div class="col-span-2 flex flex-col gap-2" style="min-height:0;">
      <div class="flex items-center justify-between">
        <label class="text-sm" style="color:var(--text-secondary)">Mermaid</label>
        <button id="theme-toggle" class="btn-accent" style="padding:4px 8px;font-size:12px;">Theme</button>
      </div>
      <select id="examples" class="panel" style="padding:6px;color:var(--text-primary);background:var(--inset);"></select>
      <textarea id="code" name="code" style="flex:1;"
        hx-post="/render" hx-target="#canvas" hx-trigger="keyup changed delay:400ms, load"
        placeholder="flowchart TD&#10;  A[Start] --> B[End]">flowchart TD
  A[Start] --> B[End]</textarea>
    </div>
    <div class="col-span-10 panel" style="min-height:0;overflow:auto;padding:24px;">
      <div id="canvas"></div>
    </div>
  </div>
  <script src="/js/app.js"></script>
</body>
</html>
```

- [ ] **Step 6: Implement app.js**

`src/MermaidMcpServer/wwwroot/js/app.js`:

```javascript
mermaid.initialize({ startOnLoad: false, theme: 'dark' });

// Re-run Mermaid on the fragment returned by HTMX into #canvas.
document.body.addEventListener('htmx:afterSwap', (e) => {
  if (e.target.id !== 'canvas') return;
  const node = e.target.querySelector('.mermaid');
  if (!node) return;
  node.removeAttribute('data-processed');
  mermaid.run({ nodes: [node] }).catch((err) => {
    e.target.innerHTML = '<div class="diagram-error">' + String(err.message || err) + '</div>';
  });
});

// Theme toggle: swap data-theme and Mermaid theme.
document.getElementById('theme-toggle').addEventListener('click', () => {
  const root = document.documentElement;
  const next = root.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
  root.setAttribute('data-theme', next);
  mermaid.initialize({ startOnLoad: false, theme: next === 'dark' ? 'dark' : 'default' });
  htmx.trigger('#code', 'keyup');
});

// Load code from ?code=<base64url> (preview links from the render_mermaid tool).
(function loadFromQuery() {
  const p = new URLSearchParams(location.search).get('code');
  if (!p) return;
  const b64 = p.replace(/-/g, '+').replace(/_/g, '/');
  try { document.getElementById('code').value = decodeURIComponent(escape(atob(b64))); } catch { /* ignore */ }
})();

// Populate examples dropdown; loading one fills the textarea and re-renders.
['flowchart'].forEach((t) => {
  const opt = document.createElement('option'); opt.value = t; opt.textContent = t;
  document.getElementById('examples').appendChild(opt);
});
document.getElementById('examples').addEventListener('change', async (e) => {
  if (!e.target.value) return;
  const code = await (await fetch('/examples/' + e.target.value)).text();
  document.getElementById('code').value = code;
  htmx.trigger('#code', 'keyup');
});
```

- [ ] **Step 7: Run — expect PASS**

Run: `dotnet test --filter "ShellTests"`
Expected: 1 test passed.

- [ ] **Step 8: Manual verification (browser)**

Run: `dotnet run --project src/MermaidMcpServer`
Open `http://localhost:5000`. Confirm: left textarea + right diagram, default flowchart renders, editing re-renders (~400ms), theme toggle flips dark/light, examples dropdown loads code. Confirm Ember Orange appears only on interactive elements.

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -m "feat: two-pane web UI with Precision Instrument design system"
```

---

### Task 9: README

**Files:**
- Create: `README.md`

**Interfaces:** none (documentation).

- [ ] **Step 1: Write README.md**

`README.md` (Russian; include exactly these sections):

```markdown
# MermaidMcpServer

Сервер рендеринга Mermaid-диаграмм с двухпанельным веб-UI (слева код, справа диаграмма)
и встроенным MCP-сервером по протоколу SSE. Рендер в браузере — `mermaid.js`;
генерация кода — `FoggyBalrog.MermaidDotNet`; опциональный серверный рендер в SVG/PNG — PuppeteerSharp.

## Возможности
- Двухпанельный редактор: `<textarea>` (col-span-2) + канва (col-span-10), HTMX + Tailwind.
- Тёмная/светлая темы (дизайн-система «The Precision Instrument», импорт из DESIGN.json/DESIGN.md).
- MCP-сервер по SSE с поддержкой ~18 типов диаграмм Mermaid.

## Запуск
​```bash
dotnet run --project src/MermaidMcpServer
​```
Веб-UI: http://localhost:5000

## MCP-сервер (SSE)
Эндпоинт SSE: `http://localhost:5000/sse` (плюс `/message` для POST-сообщений).
Современные клиенты могут использовать Streamable HTTP на корне.

Пример конфигурации клиента (Claude Code / совместимые):
​```json
{ "mcpServers": { "mermaid": { "type": "sse", "url": "http://localhost:5000/sse" } } }
​```

### Инструменты
- `render_mermaid(code, format=code|svg|png)` — предпросмотр/рендер. Поддержка ~18 типов диаграмм.
- `validate_mermaid(code)` — проверка синтаксиса (требует серверного рендера).
- `create_flowchart(...)` и другие билдеры (добавляются по фазам).

Поддерживаемые типы (~18): flowchart, sequence, class, state, ER, user journey, gantt,
git graph, mind map, kanban, packet, pie, quadrant, requirement, timeline, sankey, xy, block.

## Серверный рендер (SVG/PNG)
Выключен по умолчанию. Включить: в `appsettings.json` → `"Rendering": { "EnableServerRender": true }`.
При первом запросе svg/png/validate PuppeteerSharp скачает Chromium (~150 МБ).

## Офлайн Tailwind (опционально)
По умолчанию используется Tailwind Play-CDN. Для офлайна соберите статический `app.css`
standalone-бинарём Tailwind и подключите вместо CDN-скрипта в `wwwroot/index.html`.
```

- [ ] **Step 2: Commit**

```bash
git add -A && git commit -m "docs: add README with MCP SSE install instructions"
```

---

### Task 10: create_flowchart builder tool (Phase 2 exemplar)

**Files:**
- Create: `src/MermaidMcpServer/Mcp/DiagramBuilderTools.cs`
- Modify: `src/MermaidMcpServer/Program.cs` (`.WithTools<DiagramBuilderTools>()`)
- Test: `tests/MermaidMcpServer.Tests/DiagramBuilderToolsTests.cs`

**Interfaces:**
- Produces: `[McpServerToolType] class DiagramBuilderTools` with `string CreateFlowchart(FlowchartNode[] nodes, FlowchartEdge[] edges)`; records `FlowchartNode(string Id, string Label)`, `FlowchartEdge(string From, string To, string? Label)`.

- [ ] **Step 1: Write the failing test**

`tests/MermaidMcpServer.Tests/DiagramBuilderToolsTests.cs`:

```csharp
using MermaidMcpServer.Mcp;

namespace MermaidMcpServer.Tests;

public class DiagramBuilderToolsTests
{
    [Test]
    public async Task CreateFlowchart_emits_labels_and_link()
    {
        var tools = new DiagramBuilderTools();
        var code = tools.CreateFlowchart(
            new[] { new FlowchartNode("a", "Start"), new FlowchartNode("b", "End") },
            new[] { new FlowchartEdge("a", "b", "go") });

        await Assert.That(code).Contains("Start");
        await Assert.That(code).Contains("End");
        await Assert.That(code).Contains("-->");
    }

    [Test]
    public async Task CreateFlowchart_unknown_edge_node_throws()
    {
        var tools = new DiagramBuilderTools();
        await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(
            tools.CreateFlowchart(
                new[] { new FlowchartNode("a", "Start") },
                new[] { new FlowchartEdge("a", "missing", null) })));
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

Run: `dotnet test --filter "DiagramBuilderToolsTests"`
Expected: compile/FAIL — `DiagramBuilderTools` does not exist.

- [ ] **Step 3: Implement**

`src/MermaidMcpServer/Mcp/DiagramBuilderTools.cs`:

```csharp
using System.ComponentModel;
using FoggyBalrog.MermaidDotNet;
using FoggyBalrog.MermaidDotNet.Flowchart.Model;
using ModelContextProtocol.Server;

namespace MermaidMcpServer.Mcp;

public sealed record FlowchartNode(string Id, string Label);
public sealed record FlowchartEdge(string From, string To, string? Label);

[McpServerToolType]
public sealed class DiagramBuilderTools
{
    [McpServerTool(Name = "create_flowchart")]
    [Description("Build a Mermaid flowchart from nodes and edges. Returns Mermaid source code.")]
    public string CreateFlowchart(
        [Description("Nodes: each has an Id (referenced by edges) and a display Label.")] FlowchartNode[] nodes,
        [Description("Edges: From/To reference node Ids; optional Label on the link.")] FlowchartEdge[] edges)
    {
        var builder = Mermaid.Flowchart();
        var map = new Dictionary<string, Node>(StringComparer.Ordinal);

        foreach (var n in nodes)
        {
            builder = builder.AddNode(n.Label, out var created);
            map[n.Id] = created;
        }

        foreach (var e in edges)
        {
            if (!map.TryGetValue(e.From, out var from) || !map.TryGetValue(e.To, out var to))
                throw new ArgumentException($"Edge references unknown node id: {e.From} -> {e.To}");

            builder = string.IsNullOrEmpty(e.Label)
                ? builder.AddLink(from, to)
                : builder.AddLink(from, to, e.Label);
        }

        return builder.Build();
    }
}
```

> The exact `Node` type namespace (`FoggyBalrog.MermaidDotNet.Flowchart.Model`) must be confirmed against the installed package via IntelliSense; adjust the `using` if the compiler reports it. The `out var` node handle from `AddNode` is what `AddLink` consumes (verified from the library README).

- [ ] **Step 4: Register the tool type**

In `Program.cs`, chain onto the MCP builder:

```csharp
    .WithTools<MermaidTools>()
    .WithTools<DiagramBuilderTools>();
```

- [ ] **Step 5: Run — expect PASS**

Run: `dotnet test --filter "DiagramBuilderToolsTests"`
Expected: 2 tests passed. If `Node` type/namespace differs, fix the `using` and re-run.

- [ ] **Step 6: Full suite green + commit**

Run: `dotnet test`
Expected: all pass.

```bash
git add -A && git commit -m "feat: add create_flowchart builder MCP tool"
```

---

## Follow-up plans (Phase 2 remainder + Phase 3)

This plan delivers a complete, working product: the two-pane renderer, MCP over SSE with `render_mermaid` / `validate_mermaid` (covering **all ~18 types via raw code**), and `create_flowchart` as the builder exemplar.

The remaining structured builders are additive and each needs its exact FoggyBalrog API verified from the indexed README before writing no-placeholder task code. They will be authored as their own plan(s), each task following the Task 10 pattern (record types for structured params → FoggyBalrog builder → assert emitted tokens):

- **Phase 2 remainder:** `create_sequence_diagram`, `create_class_diagram`, `create_state_diagram`, `create_er_diagram`, `create_gantt`.
- **Phase 3 (long tail):** `create_user_journey`, `create_git_graph`, `create_mindmap`, `create_kanban`, `create_packet`, `create_pie_chart`, `create_quadrant_chart`, `create_requirement`, `create_timeline`, `create_sankey`, `create_xy_chart`, `create_block`.

Each new builder also extends `ExampleCatalog.Generators` with a sample for the web UI dropdown (and the `['flowchart']` array in `app.js`).

---

## Self-Review

**Spec coverage:**
- Web UI two-pane 12-2/12-10 → Task 8 (grid-cols-12 / col-span-2 / col-span-10). ✓
- HTMX + Tailwind → Task 8 (hx-post, Play CDN). ✓
- Design system import from DESIGN.json/DESIGN.md → Task 8 (tokens.css, dark/light, Ember-only-interactive, reduced-motion). ✓
- FoggyBalrog.MermaidDotNet → Tasks 3, 10. ✓
- MCP over SSE → Task 7 (EnableLegacySse, /sse). ✓
- ~18 diagram types → covered via raw code in `render_mermaid`/`validate_mermaid` (Task 5), listed in README (Task 9), stated in tool descriptions. ✓
- Hybrid rendering (client-side + opt-in server SVG/PNG) → Tasks 4, 5, 8. ✓
- README with install-MCP-over-SSE → Task 9. ✓
- Full TDD on TUnit → every task except UI-pixel/Chromium paths (explicitly manual per Global Constraints). ✓

**Placeholder scan:** No "TBD/TODO/handle edge cases". Two flagged verification points (FoggyBalrog `-->` token in Task 3, `Node` namespace in Task 10) include concrete resolution instructions, not deferrals. Follow-up builders are a Scope-Check split (each is its own testable deliverable), not in-task placeholders.

**Type consistency:** `MermaidPreview.{ToBase64Url,FromBase64Url,BuildPreviewUrl}` used consistently (Tasks 2, 5). `HeadlessRenderer.{IsEnabled,RenderSvgAsync,RenderPngAsync,ValidateAsync}` consistent (Tasks 4, 5). `RenderingOptions.EnableServerRender` / `ServerOptions.PublicBaseUrl` consistent (Tasks 4–7). `ExampleCatalog.{Types,Get}` consistent (Tasks 3, 6). MCP tool names `render_mermaid`/`validate_mermaid`/`create_flowchart` consistent (Tasks 5, 7, 10).
