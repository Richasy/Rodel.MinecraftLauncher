using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Runs the actual release executables through Core's managed MCP owner; never substitutes a protocol peer.</summary>
[TestClass]
public sealed class ReleaseNativeTests
{
    /// <summary>
    /// Design review: PE headers alone cannot establish that an ARM64 child starts or speaks MCP.
    /// Each shipped component must initialize and expose real tools; the source-built file component must also read isolated bytes.
    /// Normal test runs skip this explicit release gate rather than downloading tools or opening player data.
    /// </summary>
    [TestMethod]
    [DataRow("rodel_files")]
    [DataRow("rodel_web")]
    [DataRow("rodel_search")]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task PackagedNativeComponent_InitializesRealProtocol_AndFilesReadRealBytes(string id)
    {
        var root = Environment.GetEnvironmentVariable("RODEL_TEST_RELEASE_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("Set RODEL_TEST_RELEASE_ROOT to the native CLI publish directory.");
        using var sandbox = new AiSandbox();
        using var components = new AiManagedComponents(root);
        var (component, executable) = await components.OpenAsync(id, TestContext.CancellationToken);
        await using var session = await AiManagedMcpSession.ConnectAsync(new AiMcpRegistration
        {
            Id = id,
            DisplayName = component.Id,
            ExecutablePath = executable,
            WorkingDirectory = sandbox.Root,
            Arguments = id == "rodel_files" ? ["--allow-write", sandbox.Root] : []
        }, (_, _, _) => Task.CompletedTask, ct: TestContext.CancellationToken);
        Assert.IsNotEmpty(session.Tools);
        if (id != "rodel_files") return;

        const string marker = "release-native-file-read-7351";
        var file = Path.Combine(sandbox.Root, "release-read.txt");
        await File.WriteAllTextAsync(file, marker, TestContext.CancellationToken);
        var tool = session.Tools.Single(item => session.ActualName(item.Name) == "read_file_lines");
        var response = await session.CallAsync(tool.Name, new JsonObject { ["path"] = file, ["offset"] = 0, ["limit"] = 4 }, TestContext.CancellationToken);
        var serialized = JsonSerializer.Serialize(response, AssistantJsonContext.Default.AiMcpCallResult);
        Assert.AreEqual("succeeded", response.Status, serialized);
        Assert.Contains(marker, serialized);
    }

    /// <summary>Gets or sets the bounded cancellation token for native child startup, protocol discovery and cleanup.</summary>
    public TestContext TestContext { get; set; }
}