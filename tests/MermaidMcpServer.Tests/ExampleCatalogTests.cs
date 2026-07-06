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
