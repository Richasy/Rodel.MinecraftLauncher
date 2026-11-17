// Copyright (c) Richasy. All rights reserved.

extern alias NativeApp;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NativeSettings = NativeApp::Rodel.MinecraftLauncher.App.Settings;
using NativeUpdates = NativeApp::Rodel.MinecraftLauncher.App.Features.Updates;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects the actual update store's durable user choice and single-use installation authorization.</summary>
[TestClass]
public sealed class UpdatePreferenceTests
{
    /// <summary>Review: client skip arguments do not prove the UI commits the choice. Reopen actual AppSettings and UpdateState over the same isolated backing store.</summary>
    [TestMethod]
    public void SkipVersion_ReopenedPreferencesSuppressOnlyThatRelease() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        var values = new Dictionary<string, object>();
        using (var settings = new NativeSettings.AppSettings(values))
        using (var state = CreateState(settings, scope.Root))
        {
            await state.CheckAsync();
            Assert.IsNotNull(state.Available);
            Assert.IsTrue(state.Skip());
            Assert.IsNull(state.Available);
        }

        using var reopened = new NativeSettings.AppSettings(values);
        using var skipped = CreateState(reopened, scope.Root);
        await skipped.CheckAsync();
        Assert.IsNull(skipped.Available);
        reopened.SkippedUpdateVersion = "0.2611.3.0";
        using var later = CreateState(reopened, scope.Root);
        await later.CheckAsync();
        Assert.IsNotNull(later.Available);
    });

    /// <summary>
    /// Review: cancellation can win after verified publication but before the dialog's continuation.
    /// It must revoke installation, not merely cancel the now-completed HTTP request. A fresh explicit
    /// attempt may hand off once; a close veto must retain the update and cannot replay that authorization.
    /// </summary>
    [TestMethod]
    public void CancelAfterDownload_RevokesHandoffAndAllowsFreshRetry() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        using var settings = new NativeSettings.AppSettings(new Dictionary<string, object>());
        using var state = CreateState(settings, scope.Root);
        await state.CheckAsync();
        var path = await state.DownloadAsync();
        Assert.IsNotNull(path);
        Assert.IsTrue(File.Exists(path));
        state.Cancel();
        var calls = 0;
        Task<bool> Handoff(string file, string sha256)
        {
            calls++;
            Assert.IsTrue(File.Exists(file));
            Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData("installer bytes"u8)), sha256);
            return Task.FromResult(false);
        }

        await state.InstallAsync(path, Handoff);
        Assert.AreEqual(0, calls);
        Assert.IsFalse(File.Exists(path));
        path = await state.DownloadAsync();
        Assert.IsNotNull(path);
        await state.InstallAsync(path, Handoff);
        Assert.AreEqual(1, calls);
        Assert.IsTrue(state.Blocked);
        Assert.IsNotNull(state.Available);
        Assert.IsFalse(File.Exists(path));
        await state.InstallAsync(path, Handoff);
        Assert.AreEqual(1, calls, "A stale continuation cannot repeat a consumed installation intent.");
    });

    private static NativeUpdates.UpdateState CreateState(NativeSettings.AppSettings settings, string root) =>
        new(settings, new HttpClient(new ReleaseHandler()), root, new(0, 2611, 4, 0));

    private sealed class ReleaseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string name = "RMCL-0.2611.5.0-win-x64-setup.exe";
            var bytes = "installer bytes"u8.ToArray();
            var manifest = new NativeUpdates.ReleaseManifest
            {
                SchemaVersion = 1,
                Version = "0.2611.5.0",
                SourceCommit = new('1', 40),
                Notes = new() { ["en"] = "English release notes" },
                Installers = new() { ["x64"] = new() { FileName = name, Size = bytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) } },
            };
            const string prefix = NativeUpdates.GitHubUpdateClient.Repository + "/releases/download/v0.2611.5.0/";
            var release = new NativeUpdates.GitHubRelease
            {
                TagName = "v0.2611.5.0",
                Assets = [new() { Name = name, Size = bytes.Length, DownloadUrl = prefix + name },
                    new() { Name = "release-note.json", Size = 1024, DownloadUrl = prefix + "release-note.json" }],
            };
            HttpContent content = request.RequestUri == NativeUpdates.GitHubUpdateClient.Latest
                ? new StringContent(JsonSerializer.Serialize(release, NativeUpdates.ReleaseJsonContext.Default.GitHubRelease), Encoding.UTF8, "application/json")
                : request.RequestUri!.AbsolutePath.EndsWith("release-note.json", StringComparison.Ordinal)
                ? new StringContent(JsonSerializer.Serialize(manifest, NativeUpdates.ReleaseJsonContext.Default.ReleaseManifest), Encoding.UTF8, "application/json")
                : new ByteArrayContent(bytes);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}