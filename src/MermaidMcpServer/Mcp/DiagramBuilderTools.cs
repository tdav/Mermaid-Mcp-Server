using System.ComponentModel;
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
        var builder = global::FoggyBalrog.MermaidDotNet.Mermaid.Flowchart();
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
                ? builder.AddLink(from, to, out _)
                : builder.AddLink(from, to, out _, e.Label);
        }

        return builder.Build();
    }
}
