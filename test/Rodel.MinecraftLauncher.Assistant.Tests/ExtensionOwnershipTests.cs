using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects user-authored skills and installer ownership without installed external clients or model accounts.</summary>
[TestClass]
public sealed class ExtensionOwnershipTests
{
    /// <summary>
    /// Legacy enabled registrations must not enlarge the fixed product catalog, execute a program, or get erased on read.
    /// Seed real persisted entries to distinguish enforcement from merely hiding the App controls; file-root authorization remains editable.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task BuiltinExtensions_IgnoreLegacyRegistrations_RejectCustomization_AndPreserveFileRoots()
    {
        using var sandbox = new AiSandbox();
        var directory = Directory.CreateDirectory(Path.Combine(sandbox.Root, "ai")).FullName;
        var file = Path.Combine(directory, "extensions.json");
        const string legacy = """{"schemaVersion":1,"revision":7,"mcp":[{"id":"old","enabled":true,"executablePath":"must-not-run"}],"skillDirectories":[{"id":"old","enabled":true,"path":"must-not-read"}],"fileRoots":[],"future":"preserve"}""";
        await File.WriteAllTextAsync(file, legacy, TestContext.CancellationToken);
        var store = new AiExtensionStore(sandbox.Paths);
        var report = await store.GetAsync(TestContext.CancellationToken);
        Assert.IsEmpty(report.Mcp!);
        Assert.IsEmpty(report.SkillDirectories!);
        var document = await store.ReadDocumentAsync(TestContext.CancellationToken);
        Assert.IsEmpty(document["mcp"]!.AsArray());
        Assert.IsEmpty(document["skillDirectories"]!.AsArray());
        var events = new List<string>();
        await using var tools = new AiExternalMcpTools(sandbox.Paths, new System.Text.Json.Nodes.JsonObject { ["mode"] = "allow_operations" },
            (kind, _, _) => { events.Add(kind); return Task.CompletedTask; });
        Assert.IsEmpty(await tools.ConnectAsync(TestContext.CancellationToken));
        Assert.IsEmpty(events); // A started/failed connection is already a forbidden attempt, even if its fake executable never launches.
        var catalog = (await AiSkills.LoadAsync(sandbox.Paths, TestContext.CancellationToken)).List();
        Assert.Contains(item => item.Id == "builtin:skin-editing", catalog.Items);
        Assert.IsTrue(catalog.Items.All(item => item.Source == "product"));
        Assert.IsFalse(catalog.UserDirectoriesImplemented);
        Assert.AreEqual(legacy, await File.ReadAllTextAsync(file, TestContext.CancellationToken));
        var skill = Assert.Throws<LauncherOperationException>(() => AiExtensionStore.SaveSkillDirectoryAsync(new() { Path = sandbox.Root, ExpectedRevision = 7 }, TestContext.CancellationToken));
        Assert.AreEqual("AI_BUILTIN_EXTENSIONS_ONLY", skill.Code);
        var mcp = Assert.Throws<LauncherOperationException>(() => AiExtensionStore.SaveMcpAsync(new() { DisplayName = "blocked", ExecutablePath = "blocked", ExpectedRevision = 7 }, TestContext.CancellationToken));
        Assert.AreEqual("AI_BUILTIN_EXTENSIONS_ONLY", mcp.Code);
        Assert.Throws<LauncherOperationException>(() => AiExtensionStore.SetEnabledAsync("mcp", "old", true, 7, TestContext.CancellationToken));
        await Assert.ThrowsAsync<LauncherOperationException>(async () => await store.RemoveAsync("skillDirectories", "old", 7, TestContext.CancellationToken));
        var changed = await store.SaveRootAsync(new() { Path = sandbox.Root, Mode = "read_only", ExpectedRevision = 7 }, TestContext.CancellationToken);
        Assert.AreEqual(8L, changed.Revision);
        Assert.AreEqual("preserve", changed.AdditionalData!["future"].GetString());
        Assert.AreEqual(1, changed.FileRoots!.Count);
    }
    /// <summary>
    /// Exercise effective runtime authorization rather than only the UI list: default roots survive forged persisted IDs,
    /// and rejected edits/removals leave the file and revision unchanged while ordinary user roots remain editable.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task BuiltinFolders_AreAlwaysAuthorized_AndCannotBeReconfigured()
    {
        using var sandbox = new AiSandbox();
        using var package = new AiSandbox();
        var paths = new Rodel.MinecraftLauncher.Core.Storage.LauncherPaths(sandbox.Root, applicationDataDirectory: package.Root);
        var store = new AiExtensionStore(paths);
        var initial = await store.ReadDocumentAsync(TestContext.CancellationToken);
        var roots = initial["fileRoots"]!.AsArray();
        Assert.HasCount(2, roots);
        Assert.AreEqual("builtin:app", roots[0]!["id"]!.ToString());
        Assert.AreEqual(package.Root, roots[0]!["path"]!.ToString());
        Assert.AreEqual("read_only", roots[0]!["mode"]!.ToString());
        Assert.AreEqual(sandbox.Paths.Root, roots[1]!["path"]!.ToString());
        Assert.AreEqual("read_write", roots[1]!["mode"]!.ToString());
        var saved = await store.SaveRootAsync(new() { Path = sandbox.Root, Mode = "read_only", ExpectedRevision = 0 }, TestContext.CancellationToken);
        saved.FileRoots!.Add(new System.Text.Json.Nodes.JsonObject { ["id"] = "builtin:app", ["path"] = sandbox.Root, ["mode"] = "read_write" });
        // A manually authored root is still readable; reserving product IDs must not impose a new GUID-only read gate.
        saved.FileRoots!.Add(new System.Text.Json.Nodes.JsonObject { ["id"] = "manual:logs", ["path"] = sandbox.Root, ["mode"] = "read_only", ["future"] = true });
        var file = Path.Combine(sandbox.Root, "ai", "extensions.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(saved, AiJsonContext.Default.AiExtensionReport), TestContext.CancellationToken);
        var original = await File.ReadAllTextAsync(file, TestContext.CancellationToken);
        foreach (var id in new[] { "builtin:app", "builtin:data" })
        {
            var edit = await Assert.ThrowsAsync<LauncherOperationException>(async () => await store.SaveRootAsync(new() { Id = id, Path = sandbox.Root, Mode = "read_write", ExpectedRevision = 1 }, TestContext.CancellationToken));
            Assert.AreEqual("AI_BUILTIN_ROOT_IMMUTABLE", edit.Code);
            var remove = await Assert.ThrowsAsync<LauncherOperationException>(async () => await store.RemoveAsync("fileRoots", id, 1, TestContext.CancellationToken));
            Assert.AreEqual("AI_BUILTIN_ROOT_IMMUTABLE", remove.Code);
        }

        Assert.AreEqual(original, await File.ReadAllTextAsync(file, TestContext.CancellationToken));
        var effective = await store.GetAsync(TestContext.CancellationToken);
        Assert.HasCount(4, effective.FileRoots!);
        Assert.Contains(root => root["id"]?.ToString() == "manual:logs" && root["future"]?.GetValue<bool>() == true, effective.FileRoots!.OfType<System.Text.Json.Nodes.JsonObject>());
        Assert.AreEqual("read_only", effective.FileRoots![0]!["mode"]!.ToString());
        var userId = saved.FileRoots![0]!["id"]!.ToString();
        await store.RemoveAsync("fileRoots", userId, 1, TestContext.CancellationToken);
        Assert.HasCount(3, (await store.ReadDocumentAsync(TestContext.CancellationToken))["fileRoots"]!.AsArray());
    }

    /// <summary>
    /// Skills-only installation must not need or launch a client. Stale plans fail, and removal preserves manual edits and unrelated files.
    /// This intentionally omits Codex/Claude command execution: that would impose an external-program prerequisite on these regressions.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task SkillsOnlyInstall_RejectsStalePlan_AndRemovalPreservesManualContent()
    {
        using var sandbox = new AiSandbox();
        var config = Directory.CreateDirectory(Path.Combine(sandbox.Root, "client-config")).FullName;
        var unrelated = Path.Combine(config, ".claude.json");
        const string existingConfig = """{"userCustomField":{"preserve":913},"mcpServers":{"foreign":{"command":"do-not-run"}}}""";
        await File.WriteAllTextAsync(unrelated, existingConfig, TestContext.CancellationToken);
        var clientTripwire = Path.Combine(sandbox.Root, "must-not-launch.exe");
        await File.WriteAllTextAsync(clientTripwire, "Not an executable: skills-only must never launch this path.", TestContext.CancellationToken);
        var service = new AiClientIntegrationService(sandbox.Paths);
        var request = new AiClientIntegrationRequest
        {
            Client = "claude",
            ClientExecutable = clientTripwire,
            SkillsOnly = true,
            ClientConfigRoot = config,
            SkillsRoot = Path.Combine(sandbox.Root, "skills"),
            InstallationRoot = Path.Combine(sandbox.Root, "installation")
        };
        var plan = await service.PlanAsync(request, TestContext.CancellationToken);
        Assert.AreEqual("skills_only", plan.McpAction);
        var installed = await service.ApplyAsync(request with { PlanFingerprint = plan.Fingerprint }, TestContext.CancellationToken);
        Assert.AreEqual("completed", installed.Status);
        Assert.IsFalse(installed.Mcp.Registered);
        var product = Path.Combine(request.SkillsRoot, "rodel-minecraft");
        var index = Path.Combine(product, "SKILL.md");
        Assert.IsTrue(File.Exists(index));
        var stalePlan = await service.PlanAsync(request, TestContext.CancellationToken);
        const string manual = "User's edited skill must survive removal.";
        await File.AppendAllTextAsync(index, "\n" + manual, TestContext.CancellationToken);
        var failure = await Assert.ThrowsAsync<LauncherOperationException>(async () =>
            await service.ApplyAsync(request with { PlanFingerprint = stalePlan.Fingerprint }, TestContext.CancellationToken));
        Assert.AreEqual("AI_INTEGRATION_PLAN_CHANGED", failure.Code);
        await File.WriteAllTextAsync(Path.Combine(product, "manual.txt"), "unowned", TestContext.CancellationToken);
        var remove = request with { Action = "remove" };
        var review = await service.PlanAsync(remove, TestContext.CancellationToken);
        var removed = await service.ApplyAsync(remove with { PlanFingerprint = review.Fingerprint }, TestContext.CancellationToken);
        Assert.AreEqual("partial", removed.Status);
        Assert.Contains(manual, await File.ReadAllTextAsync(index, TestContext.CancellationToken));
        Assert.AreEqual("unowned", await File.ReadAllTextAsync(Path.Combine(product, "manual.txt"), TestContext.CancellationToken));
        Assert.IsFalse(File.Exists(Path.Combine(product, "references", "launcher-basics.md")));
        Assert.IsFalse(File.Exists(Path.Combine(product, "references", "skin-editing.md")));
        Assert.AreEqual(existingConfig, await File.ReadAllTextAsync(unrelated, TestContext.CancellationToken));
    }

    /// <summary>
    /// Author review: App no longer carries a CLI. A missing selection must produce an actionable plan without
    /// launching the client or copying a package fallback; applying it must preserve both client and skill files.
    /// The invalid native executable is a tripwire, so attempted client discovery cannot silently pass this case.
    /// </summary>
    [TestMethod]
    public async Task MissingStandaloneCli_RequiresSelectionWithoutLaunchingOrInstallingAnything()
    {
        using var sandbox = new AiSandbox();
        var tripwire = Path.Combine(sandbox.Root, "must-not-run.exe");
        await File.WriteAllTextAsync(tripwire, "Not an executable.", TestContext.CancellationToken);
        var request = new AiClientIntegrationRequest
        {
            Client = "claude",
            ClientExecutable = tripwire,
            ClientConfigRoot = Path.Combine(sandbox.Root, "client"),
            SkillsRoot = Path.Combine(sandbox.Root, "skills"),
            InstallationRoot = Path.Combine(sandbox.Root, "install")
        };
        var service = new AiClientIntegrationService(sandbox.Paths);
        var plan = await service.PlanAsync(request, TestContext.CancellationToken);
        Assert.AreEqual("cli_required", plan.McpAction);
        Assert.AreEqual(string.Empty, plan.LauncherExecutable);
        Assert.DoesNotContain(file => file.Bridge, plan.Files);
        var failure = await Assert.ThrowsExactlyAsync<LauncherOperationException>(() => service.ApplyAsync(request with { PlanFingerprint = plan.Fingerprint }, TestContext.CancellationToken));
        Assert.AreEqual("AI_INTEGRATION_CLI_REQUIRED", failure.Code);
        Assert.IsFalse(Directory.Exists(request.SkillsRoot));
        Assert.IsFalse(File.Exists(Path.Combine(request.InstallationRoot!, "claude.json")));
    }

    /// <summary>
    /// Author review: a caller-selected CLI is an external installation, not an owned copied payload. The actual
    /// planning/verification path must bind its hash and reject replacement before writing skills or registration.
    /// The installed .NET host supplies only the external client's harmless --version response; no MCP client
    /// registration or launcher execution is claimed by this frozen-plan test.
    /// The missing config-root rows also protect fresh installations from invoking client list against an absent home.
    /// </summary>
    [TestMethod]
    [DataRow("codex")]
    [DataRow("claude")]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task StandaloneCli_IsReferencedInPlace_AndChangedSourceInvalidatesTheFrozenPlan(string client)
    {
        using var sandbox = new AiSandbox();
        var cli = Path.Combine(sandbox.Root, "standalone", "rodel-mc.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(cli)!);
        await File.WriteAllTextAsync(cli, "Selected CLI version A", TestContext.CancellationToken);
        var dotnet = Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
        var request = new AiClientIntegrationRequest
        {
            Client = client,
            ClientExecutable = dotnet,
            LauncherExecutable = cli,
            ClientConfigRoot = Path.Combine(sandbox.Root, "client"),
            SkillsRoot = Path.Combine(sandbox.Root, "skills"),
            InstallationRoot = Path.Combine(sandbox.Root, "install")
        };
        var service = new AiClientIntegrationService(sandbox.Paths);
        var plan = await service.PlanAsync(request, TestContext.CancellationToken);
        Assert.AreEqual(cli, plan.LauncherExecutable);
        Assert.AreEqual(cli, plan.ConfigurationExport.Command);
        Assert.IsNotNull(plan.LauncherSha256);
        Assert.IsNull(plan.InspectionError);
        Assert.IsFalse(Directory.Exists(request.ClientConfigRoot));
        CollectionAssert.Contains(plan.ConfigurationExport.Args, "discovery");
        Assert.DoesNotContain(file => file.Bridge || file.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase), plan.Files);
        await File.WriteAllTextAsync(cli, "Selected CLI version B", TestContext.CancellationToken);
        var failure = await Assert.ThrowsExactlyAsync<LauncherOperationException>(() => service.ApplyAsync(request with { PlanFingerprint = plan.Fingerprint }, TestContext.CancellationToken));
        Assert.AreEqual("AI_INTEGRATION_PLAN_CHANGED", failure.Code);
        Assert.AreEqual("Selected CLI version B", await File.ReadAllTextAsync(cli, TestContext.CancellationToken));
        Assert.IsFalse(Directory.Exists(request.SkillsRoot));
    }

    /// <summary>Gets or sets the deadline for isolated extension and ownership changes.</summary>
    public TestContext TestContext { get; set; }
}