using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Exercises package selection and pinned payload integrity without executing synthetic bytes as a native MCP.</summary>
[TestClass]
public sealed class ManagedPackageTests
{
    /// <summary>
    /// A matching package must open on the actual process architecture, including native ARM64.
    /// After its pin is released, altered payload bytes must still be rejected; accepting all manifests is not a valid fix.
    /// </summary>
    [TestMethod]
    public async Task MatchingProcessPackage_OpensVerifiedPayload_ThenRejectsChangedBytes()
    {
        using var sandbox = new AiSandbox();
        var platform = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        var executable = await WriteManifestAsync(sandbox.Root, platform);
        await File.WriteAllBytesAsync(executable, [1, 2, 3], TestContext.CancellationToken);
        using (var components = new AiManagedComponents(sandbox.Root))
        {
            var (Component, Executable) = await components.OpenAsync("rodel_files", TestContext.CancellationToken);
            Assert.AreEqual(executable, Executable);
        }

        await File.WriteAllBytesAsync(executable, [1, 2, 4], TestContext.CancellationToken);
        using var changed = new AiManagedComponents(sandbox.Root);
        var failure = await Assert.ThrowsAsync<LauncherOperationException>(
            async () => await changed.OpenAsync("rodel_files", TestContext.CancellationToken));
        Assert.AreEqual("AI_MANAGED_MCP_HASH_MISMATCH", failure.Code);
    }

    /// <summary>Rejects another supported architecture before touching its absent executable, rather than discovering the mismatch at process launch.</summary>
    [TestMethod]
    public async Task OtherProcessArchitecture_IsRejectedBeforeOpeningItsExecutable()
    {
        using var sandbox = new AiSandbox();
        var otherPlatform = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-x64" : "win-arm64";
        await WriteManifestAsync(sandbox.Root, otherPlatform);
        using var components = new AiManagedComponents(sandbox.Root);
        var failure = await Assert.ThrowsAsync<LauncherOperationException>(
            async () => await components.OpenAsync("rodel_files", TestContext.CancellationToken));
        Assert.AreEqual("AI_MANAGED_MCP_VERSION_UNSUPPORTED", failure.Code);
    }

    private async Task<string> WriteManifestAsync(string root, string platform)
    {
        var directory = Path.Combine(root, "Assets", "AiMcp", "rodel_files");
        Directory.CreateDirectory(directory);
        var component = new AiManagedComponent("rodel_files", "fixture", "fixture", "fixture", "MIT",
            @"rodel_files\fixture.exe", Convert.ToHexString(SHA256.HashData([1, 2, 3])));
        var manifest = new AiManagedManifest(1, platform, [component]);
        await File.WriteAllBytesAsync(Path.Combine(root, "Assets", "AiMcp", "runtime.json"),
            JsonSerializer.SerializeToUtf8Bytes(manifest, AiManagedJsonContext.Default.AiManagedManifest), TestContext.CancellationToken);
        return Path.Combine(directory, "fixture.exe");
    }

    /// <summary>Gets or sets the cancellation source for this case's isolated package files.</summary>
    public TestContext TestContext { get; set; }
}