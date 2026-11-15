// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Exercises real PowerShell package audits and Store delivery with synthetic packages and a substituted network transport.</summary>
[TestClass]
public sealed class StoreReleaseWorkflowTests
{
    /// <summary>Gets or sets cancellation for the owned PowerShell child and isolated release files.</summary>
    public TestContext TestContext { get; set; } = null!;

    private static readonly string[] expected = ["arm64.msixupload", "x64.msixupload"];

    /// <summary>
    /// A receipt observer may retain a real shared-delete snapshot while the publisher records its next phase.
    /// The old move-overwrite path denies this on Windows; the reader must retain old bytes while a fresh read
    /// sees the new durable record. No network or timing assumption is needed to reach the held-handle boundary.
    /// </summary>
    [TestMethod]
    public async Task ReceiptReplacement_RetainsExistingReaderAndPublishesNewSnapshot()
    {
        var result = await RunAsync("receipt-reader");
        Assert.IsFalse(result["caught"]!.GetValue<bool>(), result["nativeError"]?.ToJsonString());
        Assert.AreEqual("before", result["retainedState"]!.GetValue<string>());
        Assert.AreEqual("after", result["currentState"]!.GetValue<string>());
        Assert.AreEqual(0, result["temporaryFiles"]!.GetValue<int>());
    }

    /// <summary>
    /// Existing delivery cases replace HTTP and cannot detect PowerShell's implicit form type for an empty POST.
    /// Capture the actual native transport on an owned loopback peer: Store create/commit remain bodyless JSON,
    /// while explicitly changing that default must not relabel OAuth forms, JSON updates or Azure ZIP uploads.
    /// </summary>
    [TestMethod]
    [DataRow("http-empty", "application/json", "")]
    [DataRow("http-form", "application/x-www-form-urlencoded", "grant_type=client_credentials&client_id=synthetic")]
    [DataRow("http-json", "application/json", "{\"value\":\"synthetic\"}")]
    [DataRow("http-upload", "application/zip", "synthetic-zip")]
    public async Task NativeHttp_UsesApiJsonWithoutChangingFormOrZipPayloads(string scenario, string contentType, string body)
    {
        var result = await RunAsync(scenario);
        Assert.IsFalse(result["caught"]!.GetValue<bool>(), result["failure"]?.GetValue<string>());
        Assert.AreEqual(contentType, result["contentType"]!.GetValue<string>());
        Assert.AreEqual(body, result["body"]!.GetValue<string>());
    }

    /// <summary>Real HTTP rejection must expose an actionable service diagnostic without disclosing bearer tokens or signed URLs.</summary>
    [TestMethod]
    public async Task NativeHttp_RejectionKeepsDiagnosticButRedactsSecrets()
    {
        var result = await RunAsync("http-rejected");
        Assert.IsTrue(result["caught"]!.GetValue<bool>());
        var failure = result["failure"]!.GetValue<string>();
        Assert.Contains("HTTP 400", failure);
        Assert.Contains("API rejected", failure);
        Assert.DoesNotContain("synthetic-bearer-token", failure);
        Assert.DoesNotContain("synthetic-sas", failure);
    }

    /// <summary>
    /// Author review: existing transport cases bypass final package auditing. Run the actual verifier under the
    /// publisher's strict mode using small synthetic ZIP/PE payloads and the real architecture manifests, whose ZIP
    /// records omit archiveFormat. Both architectures must pass while a changed ARM64 source recipe or a wrong
    /// packaged display name must fail, even when the version, identity and binary architecture are otherwise valid.
    /// This protects audit dispatch and provenance, not NativeAOT execution or the validity of synthetic media.
    /// </summary>
    [TestMethod]
    [DataRow("audit-x64", "x64", null)]
    [DataRow("audit-arm64", "arm64", null)]
    [DataRow("audit-tampered-source", "arm64", "MCP source build recipe mismatch")]
    [DataRow("audit-wrong-name", "arm64", "Final package display name")]
    public async Task PackageAudit_InheritsStrictModeAndRetainsSourceProvenance(string scenario, string architecture, string? expectedFailure)
    {
        var result = await RunAsync(scenario);
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

    /// <summary>
    /// A release must upload both exact package files, replace old packages, and preserve descriptions, images and unknown
    /// Store fields. Explicitly skipping AI changes no existing notes. The peer adds server-owned metadata and reorders
    /// packages to distinguish preservation from a strict echo, without excusing altered authored fields.
    /// This crosses real ZIP/receipt publication; only HTTP is substituted, so it cannot establish live Store certification.
    /// </summary>
    [TestMethod]
    [DataRow("generated-notes", "New English notes", "New Chinese notes")]
    [DataRow("preserve-notes", "Old English notes", "Old Chinese notes")]
    [DataRow("server-normalized", "Old English notes", "Old Chinese notes")]
    public async Task Update_PreservesStoreAssetsAndUploadsBothFrozenArchitectures(string scenario, string english, string chinese)
    {
        var result = await RunAsync(scenario);
        Assert.IsFalse(result["caught"]!.GetValue<bool>(), result["failure"]?.GetValue<string>());
        Assert.IsTrue(result["zipBytesCorrect"]!.GetValue<bool>());
        CollectionAssert.AreEqual(expected, result["zipNames"]!.AsArray().Select(v => v!.GetValue<string>()).ToArray());
        var payload = result["payload"]!;
        Assert.AreEqual("Immediate", payload["targetPublishMode"]!.GetValue<string>());
        Assert.AreEqual("PendingDelete", payload["applicationPackages"]![0]!["fileStatus"]!.GetValue<string>());
        Assert.AreEqual(2, payload["applicationPackages"]!.AsArray().Count(p => p!["fileStatus"]!.GetValue<string>() == "PendingUpload"));
        Assert.AreEqual("keep", payload["applicationPackages"]![0]!["unknownPackageField"]!.GetValue<string>());
        Assert.IsTrue(payload["pricing"]!["futureField"]!["keep"]!.GetValue<bool>());
        Assert.AreEqual("Free", payload["pricing"]!["priceId"]!.GetValue<string>());
        Assert.IsNull(payload["pricing"]!["isAdvancedPricingModel"], "A server-owned capability flag is not an authored price.");
        var listings = payload["listings"]!;
        Assert.IsNull(listings["en"]!["baseListing"]!["title"], "A packaged language must derive its name from MSIX, not a conflicting listing title.");
        Assert.AreEqual("RMCL", listings["en"]!["baseListing"]!["shortTitle"]!.GetValue<string>());
        Assert.AreEqual("RMCL", listings["en"]!["baseListing"]!["voiceTitle"]!.GetValue<string>());
        Assert.AreEqual("RMCL", listings["en"]!["baseListing"]!["sortTitle"]!.GetValue<string>());
        Assert.AreEqual("Existing English description", listings["en"]!["baseListing"]!["description"]!.GetValue<string>());
        Assert.AreEqual("https://example.invalid/already-uploaded.png", listings["en"]!["baseListing"]!["images"]![0]!["fileName"]!.GetValue<string>());
        Assert.IsTrue(listings["en"]!["platformOverrides"]!["Desktop"]!["unchanged"]!.GetValue<bool>());
        Assert.AreEqual(42, listings["zh-cn"]!["baseListing"]!["unknownListingField"]!.GetValue<int>());
        Assert.AreEqual(english, listings["en"]!["baseListing"]!["releaseNotes"]!.GetValue<string>());
        Assert.AreEqual(chinese, listings["zh-cn"]!["baseListing"]!["releaseNotes"]!.GetValue<string>());
        Assert.IsTrue(result["receipt"]!["committed"]!.GetValue<bool>());
        Assert.IsTrue(result["receipt"]!["accepted"]!.GetValue<bool>());
        Assert.IsTrue(result["receipt"]!["packagesVerified"]!.GetValue<bool>());
        Assert.IsFalse(result["receipt"]!["published"]!.GetValue<bool>());
        Assert.IsFalse(result["temporaryZipExists"]!.GetValue<bool>());
    }

    /// <summary>
    /// Accepting additive Store metadata must not allow a different package, rewritten notes or changed pricing.
    /// Mutate the persisted GET snapshot, not the submitted object or PUT echo, and require a durable uncommitted receipt.
    /// </summary>
    [TestMethod]
    [DataRow("server-changed-package")]
    [DataRow("server-changed-listing")]
    [DataRow("server-changed-pricing")]
    public async Task StoreChangesAuthoredData_PreservesDraftWithoutCommit(string scenario)
    {
        var result = await RunAsync(scenario);
        Assert.IsTrue(result["caught"]!.GetValue<bool>());
        Assert.AreEqual("updatingDraft", result["receipt"]!["phase"]!.GetValue<string>());
        Assert.IsFalse(result["receipt"]!["commitRequested"]!.GetValue<bool>());
        var calls = result["calls"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray();
        CollectionAssert.DoesNotContain(calls, "Commit draft");
        CollectionAssert.DoesNotContain(calls, "Create GitHub release");
    }

    /// <summary>
    /// An accepted commit does not prove that Store parsed the new packages. Wait past preprocessing and independently
    /// inspect the real resource boundary for both versions/architectures and all package-derived names.
    /// Review: the live API omits package-derived titles. Accept that shape only with returned package-language coverage;
    /// a missing listing, explicit null/wrong title or missing package language must still block GitHub without replaying commit.
    /// </summary>
    [TestMethod]
    [DataRow("processing-delayed", true)]
    [DataRow("processed-title-omitted", true)]
    [DataRow("processed-version-mismatch", false)]
    [DataRow("processed-title-mismatch", false)]
    [DataRow("processed-title-null", false)]
    [DataRow("processed-language-missing", false)]
    [DataRow("processed-listing-missing", false)]
    public async Task CommittedSubmission_RequiresProcessedPackageAndNameEvidence(string scenario, bool verified)
    {
        var result = await RunAsync(scenario);
        Assert.AreEqual(!verified, result["caught"]!.GetValue<bool>(), result["failure"]?.GetValue<string>());
        var calls = result["calls"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray();
        Assert.ContainsSingle(value => value == "Create draft", calls);
        Assert.ContainsSingle(value => value == "Upload ZIP", calls);
        Assert.ContainsSingle(value => value == "Commit draft", calls);
        Assert.IsTrue(result["receipt"]!["committed"]!.GetValue<bool>());
        Assert.AreEqual(verified, result["receipt"]!["packagesVerified"]!.GetValue<bool>());
        if (verified)
        {
            if (scenario == "processing-delayed")
                Assert.AreEqual(2, calls.Count(value => value == "Get submission status"));
            Assert.AreEqual(2, result["receipt"]!["processedPackages"]!.AsArray().Count);
            Assert.AreEqual("Certification", result["receipt"]!["status"]!.GetValue<string>());
        }
        else
        {
            Assert.AreEqual("processedDataMismatch", result["receipt"]!["phase"]!.GetValue<string>());
            Assert.IsTrue(result["githubRejected"]!.GetValue<bool>());
            CollectionAssert.DoesNotContain(calls, "Create GitHub release");
        }
    }

    /// <summary>
    /// An existing user draft, a changed package or inactive AI configuration must block before any Store mutation.
    /// These distinct failure boundaries would otherwise overwrite manual work, submit unreviewed bytes or send an unintended provider request.
    /// </summary>
    [TestMethod]
    [DataRow("existing-draft")]
    [DataRow("tampered-package")]
    [DataRow("ai-disabled")]
    public async Task InvalidPrerequisite_LeavesStoreAndExistingWorkUntouched(string scenario)
    {
        var result = await RunAsync(scenario);
        Assert.IsTrue(result["caught"]!.GetValue<bool>());
        Assert.IsNull(result["receipt"]);
        Assert.IsFalse(result["notesCreated"]!.GetValue<bool>());
        var calls = result["calls"]!.AsArray().Select(v => v!.GetValue<string>()).ToArray();
        CollectionAssert.DoesNotContain(calls, "Create draft");
        CollectionAssert.DoesNotContain(calls, "Upload ZIP");
        CollectionAssert.DoesNotContain(calls, "Commit draft");
    }

    /// <summary>
    /// A lost commit response is an unknown effect, not permission to resend. The fixture throws at the actual commit
    /// transport boundary, then explicitly retries the publisher with the durable receipt and checks no second write occurs.
    /// </summary>
    [TestMethod]
    public async Task LostCommitResponse_RetainsExactDraftAndRejectsReplay()
    {
        var result = await RunAsync("commit-response-lost");
        Assert.IsTrue(result["caught"]!.GetValue<bool>());
        Assert.IsTrue(result["retryCaught"]!.GetValue<bool>());
        var receipt = result["receipt"]!;
        Assert.AreEqual("123", receipt["submissionId"]!.GetValue<string>());
        Assert.AreEqual("committingDraft", receipt["phase"]!.GetValue<string>());
        Assert.IsTrue(receipt["commitRequested"]!.GetValue<bool>());
        Assert.IsFalse(receipt["committed"]!.GetValue<bool>());
        var calls = result["calls"]!.AsArray().Select(v => v!.GetValue<string>()).ToArray();
        Assert.ContainsSingle(c => c == "Create draft", calls);
        Assert.ContainsSingle(c => c == "Commit draft", calls);
    }

    /// <summary>The live API omits an absent pending draft; strict-mode preflight must still resolve the published source and increment its version.</summary>
    [TestMethod]
    public async Task Preflight_WithoutOptionalPendingFieldResolvesPublishedBaseline()
    {
        var result = await RunAsync("preflight");
        Assert.IsFalse(result["caught"]!.GetValue<bool>(), result["failure"]?.GetValue<string>());
        Assert.AreEqual("0.2611.1.0", result["preflight"]!["version"]!.GetValue<string>());
        Assert.IsNull(result["receipt"]);
    }

    /// <summary>
    /// GitHub publication must describe the same accepted Store source and frozen notes, without uploading packages.
    /// Repeating only this delivery must reuse the exact existing Release, never commit Store again or duplicate the Release.
    /// </summary>
    [TestMethod]
    [DataRow("github-generated", true)]
    [DataRow("github-preserve", false)]
    public async Task GitHubRelease_UsesAcceptedSourceAndReusesMatchingDelivery(string scenario, bool hasNotes)
    {
        var result = await RunAsync(scenario);
        Assert.IsFalse(result["caught"]!.GetValue<bool>(), result["failure"]?.GetValue<string>());
        var release = result["githubRelease"]!;
        Assert.AreEqual(result["receipt"]!["sourceCommit"]!.GetValue<string>(), release["target_commitish"]!.GetValue<string>());
        Assert.IsFalse(release["draft"]!.GetValue<bool>());
        Assert.IsFalse(release["prerelease"]!.GetValue<bool>());
        var body = release["body"]!.GetValue<string>();
        Assert.Contains("Certification and publication may still be pending.", body);
        Assert.AreEqual(hasNotes, body.Contains("New English notes", StringComparison.Ordinal));
        Assert.Contains("notesSha256=", body);
        var calls = result["calls"]!.AsArray().Select(v => v!.GetValue<string>()).ToArray();
        Assert.ContainsSingle(c => c == "Create GitHub release", calls);
        Assert.ContainsSingle(c => c == "Commit draft", calls);
        CollectionAssert.DoesNotContain(calls, "Upload GitHub asset");
    }

    /// <summary>Pending Store acceptance, changed notes and a conflicting tag must never produce a misleading official Release or overwrite existing work.</summary>
    [TestMethod]
    [DataRow("github-pending")]
    [DataRow("github-notes-changed")]
    [DataRow("github-source-mismatch")]
    public async Task GitHubRelease_RejectsUnboundOrUnacceptedSource(string scenario)
    {
        var result = await RunAsync(scenario);
        Assert.IsTrue(result["caught"]!.GetValue<bool>());
        Assert.IsNull(result["githubRelease"]);
        CollectionAssert.DoesNotContain(result["calls"]!.AsArray().Select(v => v!.GetValue<string>()).ToArray(), "Create GitHub release");
    }

    /// <summary>
    /// After an unknown commit response, observing acceptance for that exact draft may finish GitHub delivery only,
    /// including when its names are package-derived and the optional listing title is absent.
    /// The original lack of acknowledgement remains recorded; repeated finalization must not resend a Store write.
    /// </summary>
    [TestMethod]
    public async Task GitHubResume_ConfirmsUnknownCommitWithoutReplayingStoreWrites()
    {
        var result = await RunAsync("github-resume-commit");
        Assert.IsTrue(result["caught"]!.GetValue<bool>());
        Assert.IsTrue(result["retryCaught"]!.GetValue<bool>());
        Assert.IsNotNull(result["githubRelease"], result["failure"]?.GetValue<string>());
        Assert.IsTrue(result["receipt"]!["commitConfirmedByStatus"]!.GetValue<bool>());
        Assert.IsFalse(result["receipt"]!["committed"]!.GetValue<bool>());
        var calls = result["calls"]!.AsArray().Select(v => v!.GetValue<string>()).ToArray();
        Assert.ContainsSingle(c => c == "Create draft", calls);
        Assert.ContainsSingle(c => c == "Upload ZIP", calls);
        Assert.ContainsSingle(c => c == "Update draft", calls);
        Assert.ContainsSingle(c => c == "Commit draft", calls);
        Assert.ContainsSingle(c => c == "Create GitHub release", calls);
    }

    private async Task<JsonObject> RunAsync(string scenario)
    {
        JsonObject? result = null;
        await ReportStorage.RunAsync(async paths =>
        {
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, ".github", "store-release-baseline.json")))
                repository = repository.Parent;
            Assert.IsNotNull(repository);
            var start = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(repository.FullName, "test", "Fixtures", "Release", "store-release.ps1"), "-Repository", repository.FullName, "-Directory", paths.Root, "-Scenario", scenario })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(TestContext.CancellationToken);
                var error = process.StandardError.ReadToEndAsync(TestContext.CancellationToken);
                await process.WaitForExitAsync(TestContext.CancellationToken);
                Assert.AreEqual(0, process.ExitCode, await error);
                result = JsonNode.Parse(await output)!.AsObject();
            }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        });
        return result!;
    }
}