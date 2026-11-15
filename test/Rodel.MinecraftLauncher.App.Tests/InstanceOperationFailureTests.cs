// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Features.Instances;
using Rodel.MinecraftLauncher.Core.Application;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects actionable instance failures in the readable report shared by display, copy and export.</summary>
[TestClass]
public sealed class InstanceOperationFailureTests
{
    /// <summary>Durable, legacy and immediate failures must not disappear behind a generic status summary.</summary>
    [TestMethod]
    [DataRow("structured")]
    [DataRow("legacy")]
    [DataRow("immediate")]
    public void ReadableReport_PreservesFailureEvidence(string source)
    {
        const string message = "Cannot delete the requested directory: access denied.";
        var state = new InstanceManagementState
        {
            Action = InstanceAction.Delete,
            StatusCode = "failed",
            Error = source == "immediate" ? message : string.Empty,
            ErrorCode = source == "immediate" ? "ACCESS_DENIED" : string.Empty,
            Job = source == "immediate" ? null : new JobStatus
            {
                Id = "failed-removal",
                Status = "failed",
                Error = source == "legacy" ? message : "Fallback must not replace structured evidence.",
                Failure = source == "structured" ? new JsonObject
                {
                    ["error"] = new JsonObject { ["code"] = "ACCESS_DENIED", ["message"] = message }
                } : null,
            },
        };

        Assert.Contains(message, state.ReadableReport);
        if (source != "legacy")
            Assert.Contains("ACCESS_DENIED", state.ReadableReport);
        Assert.DoesNotContain("Fallback must not replace structured evidence.", state.ReadableReport);
    }

    /// <summary>An associated instance must start with file deletion off and cannot enable a request Core will reject.</summary>
    [TestMethod]
    public void Open_AssociatedInstancePreservesFilesByDefault() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        using var home = new HomeController(scope.Client);
        using var controller = new InstanceManagementController(scope.Client, home);
        var managed = await scope.Client.Instances.CreateAsync("managed", "synthetic", TestContext.CancellationToken);
        var associated = await scope.Client.Instances.CreateAsync("associated", "synthetic", TestContext.CancellationToken);
        await scope.Client.Instances.RelinkAsync(associated.Id, associated.GameDirectory, associated.MinecraftDirectory, TestContext.CancellationToken);

        await controller.OpenAsync(associated.Id, InstanceAction.Delete);
        Assert.IsFalse(controller.State.DeleteFiles);
        controller.ToggleDeleteFiles();
        Assert.IsFalse(controller.State.DeleteFiles);
        await controller.OpenAsync(managed.Id, InstanceAction.Delete);
        Assert.IsTrue(controller.State.DeleteFiles);
    });

    /// <summary>Supplies cancellation for isolated Core instance operations.</summary>
    public TestContext TestContext { get; set; }
}