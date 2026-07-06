using FoggyBalrog.MermaidDotNet;

namespace MermaidMcpServer.Mermaid;

// Sample diagram code per type, generated with FoggyBalrog. Extended incrementally.
public static class ExampleCatalog
{
    private static readonly Dictionary<string, Func<string>> Generators =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["flowchart"] = () => global::FoggyBalrog.MermaidDotNet.Mermaid
                .Flowchart()
                .AddNode("Start", out var start)
                .AddNode("Process", out var process)
                .AddNode("End", out var end)
                .AddLink(start, process, out _)
                .AddLink(process, end, out _)
                .Build(),
        };

    public static IReadOnlyCollection<string> Types => Generators.Keys;

    public static string? Get(string type)
        => Generators.TryGetValue(type, out var generate) ? generate() : null;
}
