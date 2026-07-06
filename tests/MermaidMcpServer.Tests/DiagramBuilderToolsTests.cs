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

        // Sync delegate wrapped directly (not via Task.FromResult) so the synchronous
        // throw from CreateFlowchart is actually caught by the assertion.
        await Assert.That(() => tools.CreateFlowchart(
                new[] { new FlowchartNode("a", "Start") },
                new[] { new FlowchartEdge("a", "missing", null) }))
            .Throws<ArgumentException>();
    }
}
