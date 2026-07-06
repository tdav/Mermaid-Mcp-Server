namespace MermaidMcpServer.Tests;

public class SmokeTests
{
    [Test]
    public async Task Test_harness_runs()
    {
        await Assert.That(1 + 1).IsEqualTo(2);
    }
}
