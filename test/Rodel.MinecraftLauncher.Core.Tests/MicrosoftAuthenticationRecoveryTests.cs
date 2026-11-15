// Copyright (c) Richasy. All rights reserved.

using Microsoft.Identity.Client;
using Rodel.MinecraftLauncher.Core.Application;
using System.Security.Authentication;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects launcher-owned Microsoft recovery classification; no Windows accounts or live authentication are accessed.</summary>
[TestClass]
public sealed class MicrosoftAuthenticationRecoveryTests
{
    /// <summary>Gets or sets the test execution context used to cancel the authentication exchange.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Only a WAM runtime initialization failure may expose the browser alternative. Reauthorization must retain its
    /// separate code, and registration/network failures must not masquerade as a broken broker or expired session.
    /// Existing external-account tests do not cross the MSAL error boundary; these synthetic exceptions replace only
    /// the provider failure. The actual classification controls both App recovery and Core's durable login-required state.
    /// </summary>
    [TestMethod]
    [DataRow("wam_runtime_init_failed", "AUTH_BROKER_UNAVAILABLE")]
    [DataRow("invalid_client", "AUTH_MICROSOFT_FAILED")]
    [DataRow("request_timeout", "AUTH_MICROSOFT_FAILED")]
    [DataRow("interaction_required", "AUTH_LOGIN_REQUIRED")]
    public void ProviderFailure_PreservesDistinctRecovery(string providerCode, string expectedCode)
    {
        const string PrivateMessage = "synthetic account and provider diagnostics must stay private";
        MsalException failure = providerCode == "interaction_required"
            ? new MsalUiRequiredException(providerCode, PrivateMessage)
            : new MsalClientException(providerCode, PrivateMessage);

        var error = (LauncherOperationException)MicrosoftAuthentication.Translate(failure, CancellationToken.None);

        Assert.AreEqual(expectedCode, error.Code);
        Assert.AreEqual(providerCode, error.Details!["providerCode"]!.GetValue<string>());
        Assert.DoesNotContain(PrivateMessage, error.Message);
    }

    /// <summary>
    /// An explicit host cancellation must win even when WAM concurrently reports a runtime failure, and closing the
    /// native picker must have the same canceled outcome. Otherwise the App would present failure/recovery UI after cancel.
    /// The real canceled token reaches the classifier; no timing or Windows UI is simulated by a delay.
    /// </summary>
    [TestMethod]
    public void Cancellation_TakesPrecedenceOverFailureAndPickerDismissal()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var failure = MicrosoftAuthentication.Translate(new MsalClientException("wam_runtime_init_failed", "synthetic"), cancellation.Token);
        var canceled = (OperationCanceledException)failure;
        Assert.AreEqual(cancellation.Token, canceled.CancellationToken);

        var dismissed = MicrosoftAuthentication.Translate(new MsalClientException(MsalError.AuthenticationCanceledError, "synthetic"), CancellationToken.None);
        Assert.IsInstanceOfType<OperationCanceledException>(dismissed);
    }

    /// <summary>
    /// An authentication transport failure must identify its service and network category without treating the account as expired.
    /// The real Xbox exchange reaches the substituted HTTP peer with a synthetic token; neither that token nor arbitrary inner
    /// exception text may enter the persisted error. Existing MSAL classification tests do not cross this HTTP boundary.
    /// </summary>
    [TestMethod]
    [DataRow(HttpRequestError.SecureConnectionError, "AUTH_TLS_ERROR")]
    [DataRow(HttpRequestError.NameResolutionError, "AUTH_NETWORK_ERROR")]
    public async Task ExchangeNetworkFailure_ReportsServiceWithoutCredentialDiagnostics(HttpRequestError reason, string expectedCode)
    {
        const string PrivateToken = "synthetic-private-microsoft-token";
        const string PrivateDiagnostic = "synthetic private provider diagnostic";
        using var http = new HttpClient(new FailedTransport(new HttpRequestException(reason, PrivateDiagnostic, new AuthenticationException(PrivateDiagnostic))));
        var authentication = new MicrosoftAuthentication("synthetic-client", http);

        var error = await Assert.ThrowsAsync<LauncherOperationException>(() => authentication.ExchangeAsync(PrivateToken, [], null, TestContext.CancellationToken));

        Assert.AreEqual(expectedCode, error.Code);
        Assert.AreEqual("xbox", error.Details!["stage"]!.GetValue<string>());
        Assert.AreEqual("user.auth.xboxlive.com", error.Details["host"]!.GetValue<string>());
        Assert.AreEqual(reason.ToString(), error.Details["networkReason"]!.GetValue<string>());
        Assert.AreEqual(nameof(AuthenticationException), error.Details["causeType"]!.GetValue<string>());
        var report = LauncherOperationException.ToResult(error).ToJsonString();
        Assert.DoesNotContain(PrivateToken, report);
        Assert.DoesNotContain(PrivateDiagnostic, report);
    }

    private sealed class FailedTransport(HttpRequestException failure) : HttpMessageHandler
    {
        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(failure);
    }
}