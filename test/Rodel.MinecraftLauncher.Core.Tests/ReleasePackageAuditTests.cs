// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Retains the package/source audit regressions independently of the retired Store submission workflow.</summary>
[TestClass]
public sealed class ReleasePackageAuditTests
{
    /// <summary>Protects strict-mode dispatch, both PE architectures, source recipe provenance and actual display names; synthetic packages are not execution evidence.</summary>
    [TestMethod]
    [DataRow("audit-x64", "x64", null)]
    [DataRow("audit-arm64", "arm64", null)]
    [DataRow("audit-tampered-source", "arm64", "MCP source build recipe mismatch")]
    [DataRow("audit-wrong-name", "arm64", "Final package display name")]
    public async Task PackageAudit_InheritsStrictModeAndRetainsSourceProvenance(string scenario, string architecture, string? expectedFailure)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "scripts", "verify-release-package.ps1")))
                repository = repository.Parent;
            Assert.IsNotNull(repository);
            var start = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[]
            {
                "-NoProfile", "-NonInteractive", "-File", Path.Combine(repository.FullName, "test", "Fixtures", "Release", "package-audit.ps1"),
                "-Repository", repository.FullName, "-Directory", paths.Root, "-Scenario", scenario,
            })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)!;
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(TestContext.CancellationToken);
                var error = process.StandardError.ReadToEndAsync(TestContext.CancellationToken);
                await process.WaitForExitAsync(TestContext.CancellationToken);
                Assert.AreEqual(0, process.ExitCode, await error);
                var result = JsonNode.Parse(await output)!.AsObject();
                Assert.AreEqual(expectedFailure is not null, result["caught"]!.GetValue<bool>(), result["failure"]?.GetValue<string>());
                if (expectedFailure is not null)
                {
                    Assert.IsNull(result["audit"]);
                    Assert.Contains(expectedFailure, result["failure"]!.GetValue<string>());
                }
                else
                {
                    Assert.AreEqual(architecture, result["audit"]!["architecture"]!.GetValue<string>());
                    Assert.AreEqual(3, result["audit"]!["managedMcpComponents"]!.AsArray().Count);
                    Assert.IsFalse(result["audit"]!["signed"]!.GetValue<bool>());
                }
            }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        });
    }

    /// <summary>Gets the test deadline for package audit processes.</summary>
    public TestContext TestContext { get; set; }
}