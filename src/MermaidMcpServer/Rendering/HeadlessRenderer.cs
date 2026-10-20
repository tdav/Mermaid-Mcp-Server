using MermaidMcpServer.Options;
using Mermaider;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Svg.Skia;

namespace MermaidMcpServer.Rendering;

// Pure-managed Mermaid renderer (Mermaider). No browser, no JS runtime, no child process.
// SVG is produced natively; PNG is rasterized from that SVG via Svg.Skia/SkiaSharp.
// ponytail: RenderSvg is synchronous CPU work wrapped in Task.FromResult; offload to
// Task.Run only if diagram sizes ever make it block request threads noticeably.
public sealed class HeadlessRenderer : IAsyncDisposable
{
    // Интервалы по умолчанию (28/56) уже подписи рёбер: подпись налезает на соседние узлы.
    // 60/120 снимает это ценой ~6% по размеру холста.
    private static readonly Mermaider.Models.RenderOptions LayoutOptions = new() { NodeSpacing = 60, LayerSpacing = 120 };

    private readonly RenderingOptions options;

    public HeadlessRenderer(IOptions<RenderingOptions> options) => this.options = options.Value;

    public bool IsEnabled => this.options.EnableServerRender;

    // Separate разводит подписи (параллельные рёбра, края холста), Resolve заменяет
    // color-mix()/var() конкретными цветами, иначе вне браузера узлы чёрные и текст не виден.
    public Task<string> RenderSvgAsync(string code) =>
        Task.FromResult(SvgColorResolver.Resolve(SvgLabelSeparator.Separate(MermaidRenderer.RenderSvg(code, LayoutOptions))));

    public Task<byte[]> RenderPngAsync(string code) =>
        Task.FromResult(RasterizeToPng(SvgColorResolver.Resolve(SvgLabelSeparator.Separate(MermaidRenderer.RenderSvg(code, LayoutOptions)))));

    public Task<(bool ok, string? error)> ValidateAsync(string code)
    {
        try
        {
            MermaidRenderer.RenderSvg(code);
            return Task.FromResult<(bool ok, string? error)>((true, null));
        }
        catch (Exception e)
        {
            return Task.FromResult<(bool ok, string? error)>((false, e.Message));
        }
    }

    private static byte[] RasterizeToPng(string svg)
    {
        using var skSvg = new SKSvg();
        skSvg.FromSvg(svg);
        if (skSvg.Picture is null)
            throw new InvalidOperationException("Mermaid produced no drawable SVG to render as PNG.");

        var rect = skSvg.Picture.CullRect;
        var width = Math.Max(1, (int)Math.Ceiling(rect.Width));
        var height = Math.Max(1, (int)Math.Ceiling(rect.Height));

        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            canvas.Translate(-rect.Left, -rect.Top);
            canvas.DrawPicture(skSvg.Picture);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
