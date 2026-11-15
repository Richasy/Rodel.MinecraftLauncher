// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Settings;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout.Reactive;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects the existing merge/flush protocol through actual Core files, with barriers only at the write boundary.</summary>
[TestClass]
public sealed class PreferencePersistenceTests
{
    /// <summary>
    /// Author review: generic width persistence cannot detect loss of a nested source choice or replacement
    /// of advanced network fields. Reopening the actual store must retain the chosen source, allow switching
    /// back without removing custom rules, then reset the category to official inheritance while retaining unknown fields.
    /// </summary>
    [TestMethod]
    public void DownloadSource_ReopensAndSwitchesWithoutReplacingOtherNetworkSettings() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        await scope.Client.Settings.SetAsync("network", JsonNode.Parse("""{"proxy":"direct","future":17,"mirrors":{"https://example.invalid/":"https://mirror.invalid/"}}"""), cancellationToken: TestContext.CancellationToken);
        var leases = new WorkLeases();
        await using (var store = new CorePreferencesStore(scope.Client, () => { }, leases.Acquire))
        {
            await store.LoadAsync();
            Assert.AreEqual("official", store.State.Text(Field("network.source")));
            store.Set(Field("network.source"), "bmclapi");
            Assert.IsTrue(await store.FlushAsync());
        }

        await using var reopened = new CorePreferencesStore(scope.Client, () => { }, leases.Acquire);
        await reopened.LoadAsync();
        Assert.AreEqual("bmclapi", reopened.State.Text(Field("network.source")));
        reopened.Set(Field("network.source"), "official");
        Assert.IsTrue(await reopened.FlushAsync());
        Assert.AreEqual("official", scope.Client.Settings.GetOverrides()["network"]?["source"]?.ToString());
        Assert.AreEqual("direct", scope.Client.Settings.GetOverrides()["network"]?["proxy"]?.ToString());
        Assert.AreEqual("https://mirror.invalid/", scope.Client.Settings.GetOverrides()["network"]?["mirrors"]?["https://example.invalid/"]?.ToString());
        reopened.Reset("network");
        Assert.IsTrue(await reopened.FlushAsync());
        Assert.AreEqual("official", reopened.State.Text(Field("network.source")));
        Assert.IsFalse(scope.Client.Settings.GetOverrides()["network"]!.AsObject().ContainsKey("source"));
        Assert.AreEqual(17, scope.Client.Settings.GetOverrides()["network"]?["future"]?.GetValue<int>());
        Assert.AreEqual(0, leases.Active);
    });

    /// <summary>
    /// Edits made while a write is held must survive its receipt and be flushed next. An independent invalid
    /// draft and unknown stored data must survive both writes; callbacks alone are not evidence of persistence.
    /// </summary>
    [TestMethod]
    public void Flush_WritesNewValidInputWithoutErasingInvalidDraftOrUnknownOverrides() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        await File.WriteAllTextAsync(Path.Combine(scope.Root, "settings.json"), """{"width":640,"height":480,"vendor":{"future":[null,17]}}""", TestContext.CancellationToken);
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var writes = 0;
        var leases = new WorkLeases();
        async Task Write(string category, JsonObject changes, IReadOnlyList<string> removed, CancellationToken token)
        {
            if (Interlocked.Increment(ref writes) == 1) { entered.TrySetResult(); await release.Task; }

            await WriteCoreAsync(scope.Client, category, changes, removed, token);
        }

        await using var store = new CorePreferencesStore(scope.Client, () => { }, leases.Acquire, Write);
        await store.LoadAsync();
        store.Set(Field("width"), "800");
        var flush = store.FlushAsync();
        await entered.Task;
        try
        {
            Assert.AreEqual(1, leases.Active);
            store.Set(Field("width"), "1200");
            store.Set(Field("height"), "not-a-number");
            store.Set(Field("fullscreen"), "true");
        }
        finally { release.TrySetResult(); }

        Assert.IsTrue(await flush);
        var saved = scope.Client.Settings.GetOverrides();
        Assert.AreEqual(1200, saved["width"]!.GetValue<int>());
        Assert.AreEqual(480, saved["height"]!.GetValue<int>());
        Assert.IsTrue(saved["fullscreen"]!.GetValue<bool>());
        Assert.IsNull(saved["vendor"]!["future"]![0]);
        Assert.AreEqual(17, saved["vendor"]!["future"]![1]!.GetValue<int>());
        Assert.AreEqual("not-a-number", store.State.Changes["height"]);
        Assert.IsFalse(store.State.Changes.ContainsKey("width"));
        Assert.AreEqual(0, leases.Active);
    });

    /// <summary>Editing defers its whole category; resetting removes overrides rather than storing empty inheritance values.</summary>
    [TestMethod]
    public void EditingAndReset_PreserveCategoryDeferralAndManagedJavaInheritance() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        await File.WriteAllTextAsync(Path.Combine(scope.Root, "settings.json"),
            """{"width":640,"height":480,"javaRuntimeId":"retained","javaRuntimeExecutable":"bin/java.exe","vendor":"keep"}""", TestContext.CancellationToken);
        var leases = new WorkLeases();
        await using var store = new CorePreferencesStore(scope.Client, () => { }, leases.Acquire);
        await store.LoadAsync();
        store.Set(Field("width"), "900");
        store.Set(Field("height"), "600");
        store.SetEditing("width", true);
        Assert.IsTrue(await store.FlushAsync());
        Assert.AreEqual(640, scope.Client.Settings.GetOverrides()["width"]!.GetValue<int>());
        Assert.AreEqual(480, scope.Client.Settings.GetOverrides()["height"]!.GetValue<int>());
        Assert.IsTrue(await store.FlushAsync(finishEditing: true));
        Assert.AreEqual(900, scope.Client.Settings.GetOverrides()["width"]!.GetValue<int>());
        store.Reset("launch");
        store.Reset("java");
        Assert.IsTrue(await store.FlushAsync());
        var saved = scope.Client.Settings.GetOverrides();
        Assert.IsFalse(saved.ContainsKey("width"));
        Assert.IsFalse(saved.ContainsKey("height"));
        Assert.IsFalse(saved.ContainsKey("javaRuntimeId"));
        Assert.IsFalse(saved.ContainsKey("javaRuntimeExecutable"));
        Assert.AreEqual("keep", saved["vendor"]!.GetValue<string>());
        Assert.AreEqual(0, leases.Active);
    });

    /// <summary>A real write rejection must retain drafts and old storage, return failure to close coordination, and allow explicit retry.</summary>
    [TestMethod]
    public void WriteFailure_PreservesDraftAndStorage_ThenRetryClearsOnlyCommittedChanges() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        await File.WriteAllTextAsync(Path.Combine(scope.Root, "settings.json"), """{"width":640}""", TestContext.CancellationToken);
        var fail = true;
        var refreshes = 0;
        var leases = new WorkLeases();
        Task Write(string category, JsonObject changes, IReadOnlyList<string> removed, CancellationToken token)
            => fail ? Task.FromException(new IOException("write rejected")) : WriteCoreAsync(scope.Client, category, changes, removed, token);
        await using var store = new CorePreferencesStore(scope.Client, () => refreshes++, leases.Acquire, Write);
        await store.LoadAsync();
        store.Set(Field("width"), "1100");
        Assert.IsFalse(await store.FlushAsync(finishEditing: true));
        Assert.AreEqual("write rejected", store.State.Error);
        Assert.AreEqual("1100", store.State.Changes["width"]);
        Assert.AreEqual(640, scope.Client.Settings.GetOverrides()["width"]!.GetValue<int>());
        Assert.AreEqual(0, refreshes);
        fail = false;
        Assert.IsTrue(await store.FlushAsync(finishEditing: true));
        Assert.AreEqual(1100, scope.Client.Settings.GetOverrides()["width"]!.GetValue<int>());
        Assert.HasCount(0, store.State.Changes);
        Assert.AreEqual(string.Empty, store.State.Error);
        Assert.AreEqual(1, refreshes);
        Assert.AreEqual(0, leases.Active);
    });

    /// <summary>
    /// A non-cooperating backend may commit after UI disposal. Disposal must wait for the real gate release,
    /// without publishing that receipt, refreshing dead UI or reporting the canceled flush as successful.
    /// </summary>
    [TestMethod]
    public void DisposeDuringWrite_WaitsForResourceReleaseWithoutLateUiPublication() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var refreshes = 0;
        var leases = new WorkLeases();
        async Task Write(string category, JsonObject changes, IReadOnlyList<string> removed, CancellationToken token)
        {
            entered.TrySetResult();
            await release.Task;
            await WriteCoreAsync(scope.Client, category, changes, removed, CancellationToken.None);
        }

        await using var store = new CorePreferencesStore(scope.Client, () => refreshes++, leases.Acquire, Write);
        await store.LoadAsync();
        var notifications = 0;
        using var effect = new Effect(() => { _ = store.State; notifications++; });
        store.Set(Field("width"), "1000");
        var flush = store.FlushAsync();
        await entered.Task;
        var disposed = store.DisposeAsync().AsTask();
        var before = notifications;
        try { Assert.IsFalse(disposed.IsCompleted); }
        finally { release.TrySetResult(); }

        Assert.IsFalse(await flush);
        await disposed;
        Assert.AreEqual(before, notifications);
        Assert.AreEqual(0, refreshes);
        Assert.AreEqual(0, leases.Active);
        Assert.AreEqual(1000, scope.Client.Settings.GetOverrides()["width"]!.GetValue<int>());
    });

    private static PreferenceField Field(string key) => CorePreferenceFields.All.Single(field => field.Key == key);
    private static Task WriteCoreAsync(LauncherClient client, string category, JsonObject changes,
        IReadOnlyList<string> removed, CancellationToken token) => category == "network"
        ? client.Settings.UpdateSectionAsync("network", changes, removed, cancellationToken: token)
        : client.Settings.UpdateAsync(changes, removed, cancellationToken: token);

    private sealed class WorkLeases
    {
        private int _active;
        internal int Active => Volatile.Read(ref _active);
        internal IDisposable Acquire() { Interlocked.Increment(ref _active); return new Lease(() => Interlocked.Decrement(ref _active)); }
        private sealed class Lease(Action release) : IDisposable
        {
            /// <inheritdoc/>
            public void Dispose() => release();
        }
    }

    public TestContext TestContext { get; set; }
}