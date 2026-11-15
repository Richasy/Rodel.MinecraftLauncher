// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.IO.Compression;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects owned component publication and corruption detection; synthetic ZIPs exercise validation and are never executed as Java.</summary>
[TestClass]
public sealed class ExternalLoginComponentTests
{
    /// <summary>
    /// A failed local replacement must preserve the previously verified component, and changed owned bytes must become unavailable.
    /// Existing download cases do not cover the Java-agent manifest or this pointer publication. The independently retained SHA and path
    /// distinguish atomic replacement from a direct overwrite of the last usable artifact.
    /// </summary>
    [TestMethod]
    public async Task Import_InvalidReplacementPreservesOldArtifact_ChangedBytesAreRejected()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var source = Path.Combine(paths.Root, "selected.jar");
            using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("META-INF/MANIFEST.MF").Open()))
                    await writer.WriteAsync("Manifest-Version: 1.0\r\nPremain-Class: moe.yushi.authlibinjector.Premain\r\n");
                _ = zip.CreateEntry("moe/yushi/authlibinjector/Premain.class");
            }

            var service = new IdentityService(paths);
            var original = await service.PrepareExternalLoginComponentAsync(source, TestContext.CancellationToken);
            Assert.AreNotEqual(source, original.Path);
            await File.WriteAllTextAsync(source, "not a jar", TestContext.CancellationToken);
            var failure = await Assert.ThrowsAsync<LauncherOperationException>(() => service.PrepareExternalLoginComponentAsync(source, TestContext.CancellationToken));
            Assert.AreEqual("AUTH_COMPONENT_FAILED", failure.Code);
            Assert.AreEqual(original, service.GetExternalLoginComponent());
            await File.WriteAllTextAsync(original.Path, "tampered", TestContext.CancellationToken);
            Assert.IsNull(service.GetExternalLoginComponent());
        });
    }

    /// <summary>Gets the runner's cancellation token for owned test file operations.</summary>
    public TestContext TestContext { get; set; } = null!;
}