// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Create;
using Rodel.MinecraftLauncher.Core.Application;
using System.Globalization;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects visible progress while a large game file is still transferring.</summary>
[TestClass]
public sealed class InstancePreparationProgressTests
{
    /// <summary>
    /// Review: completed-file counts can remain fixed for minutes during a real transfer. Existing download
    /// tests cover the producer, not this presentation boundary. Bytes must remain visible and change without
    /// a completed file; the file percentage must describe its count rather than the three-phase workflow.
    /// This uses recorded-shaped snapshots, not a network or window, and does not claim native rendering.
    /// </summary>
    /// <param name="stage">A client or asset transfer; both must expose bytes before a file completes.</param>
    [TestMethod]
    [DataRow("client")]
    [DataRow("asset")]
    public void SameCompletedFiles_StillShowsTransferAdvancing_AndMatchingFilePercentage(string stage)
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var progress = new LauncherProgress
            {
                Stage = stage,
                Bytes = 16 * 1024 * 1024,
                TotalBytes = 40 * 1024 * 1024,
                CompletedFiles = 77,
                TotalFiles = 5225,
                SharedReusedFiles = 76,
                OverallPercent = 77 * 100d / 5225
            };
            var state = new CreateState { Active = true, Job = new JobStatus { Status = "running", Progress = progress } };
            Assert.Contains("1%", state.FileProgress);
            Assert.Contains("16 MiB / 40 MiB", state.PreparationProgress);

            var advanced = state with { Job = state.Job with { Progress = progress with { Bytes = 20 * 1024 * 1024 } } };
            Assert.AreEqual(state.FileProgress, advanced.FileProgress);
            Assert.AreNotEqual(state.PreparationProgress, advanced.PreparationProgress);
            Assert.Contains("20 MiB / 40 MiB", advanced.PreparationProgress);
            Assert.AreEqual(string.Empty, (advanced with { Active = false }).PreparationProgress);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }
}