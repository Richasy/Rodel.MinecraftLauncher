using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects persisted local/remote connection meaning, independently of native engine lifetime and HTTP framing.</summary>
[TestClass]
public sealed class ServiceConfigurationTests
{
    /// <summary>
    /// An offline connection edit must preserve model identity and unknown metadata; a stale editor must leave bytes untouched.
    /// This replaces the useful configuration transaction in the old S1 catch-all without requiring network reachability.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task OfflineConnectionEdit_PreservesUnknownDataAndModels_AndRejectsStaleRevision()
    {
        using var sandbox = new AiSandbox();
        var services = new AiServiceStore(sandbox.Paths);
        var created = await services.SaveServiceAsync(new AiServiceUpdate
        {
            Protocol = AiProtocols.OpenAiChat,
            BaseUrl = "https://unreachable.invalid/v1/",
            Models = [new AiModelUpdate { Model = "manual-model" }]
        }, ct: TestContext.CancellationToken);
        var service = created.Services.Single();
        var file = Path.Combine(sandbox.Root, "ai", "services.json");
        var unknown = JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.CancellationToken))!;
        unknown["future"] = true;
        unknown["services"]![0]!["futureConnection"] = "keep";
        unknown["services"]![0]!["models"]![0]!["futureModel"] = 123;
        await File.WriteAllTextAsync(file, unknown.ToJsonString(), TestContext.CancellationToken);
        await services.SaveServiceAsync(new AiServiceUpdate
        {
            Id = service.Id,
            ExpectedRevision = service.Revision,
            DisplayName = "Renamed offline"
        }, ct: TestContext.CancellationToken);
        var persisted = JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.CancellationToken))!;
        Assert.IsTrue(persisted["future"]!.GetValue<bool>());
        Assert.AreEqual("keep", persisted["services"]![0]!["futureConnection"]!.ToString());
        Assert.AreEqual(123, persisted["services"]![0]!["models"]![0]!["futureModel"]!.GetValue<int>());
        Assert.AreEqual(service.Models[0].Id, persisted["services"]![0]!["models"]![0]!["id"]!.ToString());
        var before = await File.ReadAllBytesAsync(file, TestContext.CancellationToken);
        var stale = await Assert.ThrowsAsync<LauncherOperationException>(async () => await services.SaveServiceAsync(new AiServiceUpdate
        {
            Id = service.Id,
            ExpectedRevision = service.Revision,
            DisplayName = "Stale edit"
        }, ct: TestContext.CancellationToken));
        Assert.AreEqual("AI_CONFIGURATION_CONFLICT", stale.Code);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(file, TestContext.CancellationToken));
    }

    /// <summary>
    /// Rotating a protected key must not invalidate an in-flight snapshot or delete its blob early.
    /// Actual DPAPI storage and credential leases remain real; only synthetic keys and a temporary data root are used.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task CredentialRotation_PreservesActiveSnapshot_AndCleansOnlyReleasedRetiredBlob()
    {
        using var sandbox = new AiSandbox();
        var services = new AiServiceStore(sandbox.Paths);
        const string original = "synthetic-rotation-original-78542";
        const string replacement = "synthetic-rotation-replacement-78542";
        var created = await services.SaveServiceAsync(new AiServiceUpdate
        {
            Protocol = AiProtocols.OpenAiChat,
            BaseUrl = "https://unreachable.invalid/v1/",
            Models = [new AiModelUpdate { Model = "fixture" }]
        }, secret: original, ct: TestContext.CancellationToken);
        var service = created.Services.Single();
        var directory = Path.Combine(sandbox.Root, "ai", "secrets");
        var oldBlob = Directory.GetFiles(directory, "*.bin").Single();
        Assert.DoesNotContain(original, System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(oldBlob, TestContext.CancellationToken)));
        using var inFlight = await services.AcquireAsync(service.Id, service.Models[0].Id, true, TestContext.CancellationToken);
        await services.SaveServiceAsync(new AiServiceUpdate { Id = service.Id, ExpectedRevision = service.Revision }, secret: replacement, ct: TestContext.CancellationToken);
        await services.CleanupCredentialsAsync(TestContext.CancellationToken);
        Assert.IsTrue(File.Exists(oldBlob), "The old blob must remain while a captured request owns its lease.");
        Assert.AreEqual(original, inFlight.Credential.Value);
        using var current = await services.AcquireAsync(service.Id, service.Models[0].Id, true, TestContext.CancellationToken);
        Assert.AreEqual(replacement, current.Credential.Value);
        inFlight.Dispose();
        await services.CleanupCredentialsAsync(TestContext.CancellationToken);
        Assert.IsFalse(File.Exists(oldBlob));
        Assert.HasCount(1, Directory.GetFiles(directory, "*.bin"));
        Assert.AreEqual(replacement, current.Credential.Value);
    }

    /// <summary>
    /// A stale local profile pointing to an external daemon must not silently become an Embedded request or read that daemon's key.
    /// Explicitly re-saving the connection repairs its real persisted configuration while preserving the selected model identity.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task StaleLocalEndpoint_IsRejectedBeforeCredentialLookup_AndExplicitSaveRepairsConnection()
    {
        using var sandbox = new AiSandbox();
        var services = new AiServiceStore(sandbox.Paths);
        var configuration = await services.SaveServiceAsync(new AiServiceUpdate
        {
            Protocol = AiProtocols.RodelInference,
            DisplayName = "Local",
            Models = [new AiModelUpdate { Model = "local-fixture" }]
        }, ct: TestContext.CancellationToken);
        var original = configuration.Services.Single();
        var file = Path.Combine(sandbox.Root, "ai", "services.json");
        var legacy = JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.CancellationToken))!;
        legacy["services"]![0]!["baseUrl"] = "http://127.0.0.1:11925/";
        legacy["services"]![0]!["credentialReference"] = "env:RODEL_TEST_MUST_NOT_LOOK_UP_DAEMON_KEY";
        await File.WriteAllTextAsync(file, legacy.ToJsonString(), TestContext.CancellationToken);
        var before = await File.ReadAllBytesAsync(file, TestContext.CancellationToken);

        var failure = await Assert.ThrowsAsync<LauncherOperationException>(async () =>
            await services.AcquireAsync(original.Id, original.Models[0].Id, true, TestContext.CancellationToken));
        Assert.AreEqual("AI_LOCAL_CONFIGURATION_INVALID", failure.Code);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(file, TestContext.CancellationToken));

        await services.SaveServiceAsync(new AiServiceUpdate { Id = original.Id, ExpectedRevision = original.Revision }, ct: TestContext.CancellationToken);
        using var repaired = await services.AcquireAsync(original.Id, original.Models[0].Id, true, TestContext.CancellationToken);
        Assert.AreEqual(original.Models[0].Id, repaired.Model!.Id);
        var persisted = JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.CancellationToken))!;
        Assert.IsNull(persisted["services"]![0]!["baseUrl"]);
        Assert.IsNull(persisted["services"]![0]!["credentialReference"]);
    }

    /// <summary>
    /// Local context is a worker-load choice, not an output/history budget or online API parameter.
    /// Missing legacy/new values must use the launcher budget without rewriting storage; explicit larger choices survive edits,
    /// and reset returns to the launcher default rather than the model's potentially enormous trained context.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task LocalContextBudget_PersistsIntoSnapshot_AndClearsWithoutChangingModelIdentity()
    {
        using var sandbox = new AiSandbox();
        var services = new AiServiceStore(sandbox.Paths);
        var state = await services.SaveServiceAsync(new AiServiceUpdate
        {
            Protocol = AiProtocols.RodelInference,
            Models = [new AiModelUpdate { Model = "existing-model", Temperature = 0 }]
        }, ct: TestContext.CancellationToken);
        var service = state.Services.Single();
        var binding = service.Models.Single();
        var file = Path.Combine(sandbox.Root, "ai", "services.json");
        var unconfigured = await File.ReadAllBytesAsync(file, TestContext.CancellationToken);
        Assert.IsNull(binding.ContextLength);
        using (var defaults = await services.AcquireAsync(service.Id, binding.Id, true, TestContext.CancellationToken))
            Assert.AreEqual(65536, defaults.ProviderContext().ContextLength);
        CollectionAssert.AreEqual(unconfigured, await File.ReadAllBytesAsync(file, TestContext.CancellationToken));
        state = await services.SaveModelAsync(service.Id, new AiModelUpdate { Id = binding.Id, ContextLength = 98304 },
            service.Revision, TestContext.CancellationToken);
        using (var captured = await services.AcquireAsync(service.Id, binding.Id, true, TestContext.CancellationToken))
            Assert.AreEqual(98304, captured.ProviderContext().ContextLength);
        state = await services.SaveModelAsync(service.Id, new AiModelUpdate { Id = binding.Id, MaxOutputTokens = 512 },
            state.Services.Single().Revision, TestContext.CancellationToken);
        Assert.AreEqual(98304, state.Services.Single().Models.Single().ContextLength);
        var cleared = await services.SaveModelAsync(service.Id, new AiModelUpdate { Id = binding.Id, ClearContextLength = true },
            state.Services.Single().Revision, TestContext.CancellationToken);
        Assert.IsNull(cleared.Services.Single().Models.Single().ContextLength);
        Assert.AreEqual(binding.Id, cleared.Services.Single().Models.Single().Id);
        Assert.AreEqual(0, cleared.Services.Single().Models.Single().Temperature);
        Assert.AreEqual(512, cleared.Services.Single().Models.Single().MaxOutputTokens);
        using (var reset = await services.AcquireAsync(service.Id, binding.Id, true, TestContext.CancellationToken))
            Assert.AreEqual(65536, reset.ProviderContext().ContextLength);
        var remote = await services.SaveServiceAsync(new AiServiceUpdate
        {
            Protocol = AiProtocols.OpenAiChat,
            BaseUrl = "https://unreachable.invalid/v1/",
            Models = [new AiModelUpdate { Model = "online-model" }]
        }, ct: TestContext.CancellationToken);
        var online = remote.Services.Last();
        using (var captured = await services.AcquireAsync(online.Id, online.Models.Single().Id, true, TestContext.CancellationToken))
            Assert.IsNull(captured.ProviderContext().ContextLength);
        var before = await File.ReadAllBytesAsync(file, TestContext.CancellationToken);
        await Assert.ThrowsAsync<ArgumentException>(async () => await services.SaveModelAsync(online.Id,
            new AiModelUpdate { Id = online.Models.Single().Id, ContextLength = 16384 }, online.Revision, TestContext.CancellationToken));
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(file, TestContext.CancellationToken));
    }

    /// <summary>Automatic context honors a smaller GGUF declaration, while an explicit override remains the player's choice.</summary>
    [TestMethod]
    public async Task LocalContext_GgufDeclarationCapsDefault_OverrideRemainsIndependent()
    {
        using var sandbox = new AiSandbox();
        var path = Path.Combine(sandbox.Root, "small.gguf");
        using (var file = File.Create(path))
        using (var writer = new BinaryWriter(file))
        {
            writer.Write(0x46554747u); writer.Write(3u); writer.Write(0UL); writer.Write(2UL);
            void Text(string value) { var bytes = System.Text.Encoding.UTF8.GetBytes(value); writer.Write((ulong)bytes.Length); writer.Write(bytes); }

            Text("general.architecture"); writer.Write(8u); Text("qwen3");
            Text("qwen3.context_length"); writer.Write(4u); writer.Write(8192u);
        }

        var info = await AiServiceStore.InspectLocalModelAsync(path, TestContext.CancellationToken);
        Assert.AreEqual(8192, info.DeclaredContextLength);
        var services = new AiServiceStore(sandbox.Paths);
        var state = await services.SaveServiceAsync(new() { Protocol = AiProtocols.RodelInference, Models = [new() { Model = path }, new() { Model = "catalog-profile" }] }, ct: TestContext.CancellationToken);
        var service = state.Services.Single(); var model = service.Models[0];
        using (var automatic = await services.AcquireAsync(service.Id, model.Id, true, TestContext.CancellationToken))
            Assert.AreEqual(8192, automatic.EffectiveContextLength);
        state = await services.SaveModelAsync(service.Id, new() { Id = model.Id, ContextLength = 4096 }, service.Revision, TestContext.CancellationToken);
        using (var explicitBudget = await services.AcquireAsync(service.Id, model.Id, true, TestContext.CancellationToken))
            Assert.AreEqual(4096, explicitBudget.EffectiveContextLength);
        using (var other = await services.AcquireAsync(service.Id, service.Models[1].Id, true, TestContext.CancellationToken))
            Assert.AreEqual(65536, other.EffectiveContextLength);
        Assert.IsNull((await AiServiceStore.InspectLocalModelAsync(Path.Combine(sandbox.Root, "absent.gguf"), TestContext.CancellationToken)).DeclaredContextLength);
    }

    /// <summary>Gets or sets MSTest's bounded cancellation context.</summary>
    public TestContext TestContext { get; set; }
}