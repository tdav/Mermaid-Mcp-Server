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
