using Rodel.Inference;
using Rodel.Inference.Core;
using Rodel.MinecraftLauncher.App.Features.Ai;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects native failure details at the SDK-to-UI projection boundary; no native inference is substituted as successful.</summary>
[TestClass]
public sealed class LocalInferenceDiagnosticTests
{
    /// <summary>A worker rejection must expose its actionable bounded reason, scrub secrets, and keep the general message free of private diagnostics.</summary>
    [TestMethod]
    [DataRow("request (19009 tokens) exceeds the available context size (16384 tokens), try increasing it", "AI_CONTEXT_LIMIT")]
    [DataRow("Option rejected: request has 19009 tokens and setting 16384 is invalid.", "AI_PROVIDER_OPTIONS_INVALID")]
    public void WorkerFailure_PreservesReasonForExpandedDetails_WithoutLeakingCredential(string reason, string code)
    {
        const string secret = "private-test-token";
        var context = new AiProviderContext("rodel-inference", 1, "binding", "model", secret, null, null);
        var native = new ModelLoadException(new InferenceError(InferenceErrorKind.InvalidRequest, "Worker returned 400.",
            reason + " credential=" + secret + new string('x', 5000)));
        var error = AiChatClients.SafeError(native, context);
        Assert.AreEqual(code, error.Code);
        Assert.DoesNotContain("19009", error.Message);
        var projected = AiTranscriptContent.ErrorText(new JsonObject { ["message"] = error.Message, ["details"] = error.Details!.DeepClone() });
        Assert.Contains("19009 tokens", projected);
        Assert.Contains("16384", projected);
        Assert.DoesNotContain(secret, projected);
        Assert.IsLessThanOrEqualTo(4096, error.Details!["inferenceDetail"]!.ToString().Length);
    }
}