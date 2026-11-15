using Microsoft.AspNetCore.Http;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Tests;
using System.Net;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Exercises real proxy sockets against loopback HTTP endpoints; no community executable, public DNS or account is required.</summary>
[TestClass]
public sealed class WebEgressScopeTests
{
    /// <summary>
    /// One approved endpoint must not authorize a redirect to another port or a later invocation.
    /// Actual destination hit counts distinguish enforced egress from merely recording an approval object.
    /// </summary>
    [TestMethod]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task ApprovedEndpoint_DoesNotAuthorizeRedirectOrNextCall()
    {
        var otherHits = 0;
        var approvedHits = 0;
        await using var other = await ModelEndpoint.StartAsync(async context =>
        {
            Interlocked.Increment(ref otherHits);
            await context.Response.WriteAsync("UNAPPROVED-PAYLOAD", context.RequestAborted);
        }, TestContext.CancellationToken);
        await using var approved = await ModelEndpoint.StartAsync(context =>
        {
            Interlocked.Increment(ref approvedHits);
            context.Response.Redirect(other.Endpoint.AbsoluteUri, permanent: false, preserveMethod: true);
            return Task.CompletedTask;
        }, TestContext.CancellationToken);
        await using var proxy = new AiWebEgressProxy();
        using var client = Client(proxy);
        proxy.BeginCall(new Dictionary<string, IPAddress[]> { [AiWebEgressProxy.Endpoint("127.0.0.1", approved.Endpoint.Port)] = [IPAddress.Loopback] },
            TestContext.CancellationToken);
        using var redirect = await client.PostAsync(approved.Endpoint, null, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.BadGateway, redirect.StatusCode);
        Assert.AreEqual(1, approvedHits);
        Assert.AreEqual(0, otherHits);
        Assert.IsNotEmpty(proxy.Failures);
        await proxy.EndCallAsync();
        proxy.BeginCall(new Dictionary<string, IPAddress[]>(), TestContext.CancellationToken);
        using var later = await client.PostAsync(approved.Endpoint, null, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.BadGateway, later.StatusCode);
        Assert.AreEqual(1, approvedHits, "The previous call's endpoint permission must not survive into a new invocation.");
    }

    /// <summary>
    /// Revoking an active transport after the target actually receives the request must withhold its pending response.
    /// The entered barrier, not timing or task startup, establishes an in-flight request.
    /// </summary>
    [TestMethod]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task InFlightGrantCancellation_WithholdsPendingPage()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var page = await ModelEndpoint.StartAsync(async context =>
        {
            entered.TrySetResult();
            try
            {
                await release.Task.WaitAsync(context.RequestAborted);
                await context.Response.WriteAsync("PENDING-PRIVATE-PAGE", context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        }, TestContext.CancellationToken);
        await using var proxy = new AiWebEgressProxy();
        using var grant = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        using var client = Client(proxy);
        proxy.BeginCall(new Dictionary<string, IPAddress[]> { [AiWebEgressProxy.Endpoint("127.0.0.1", page.Endpoint.Port)] = [IPAddress.Loopback] }, grant.Token);
        var request = client.PostAsync(page.Endpoint, null, TestContext.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TestContext.CancellationToken);
            await grant.CancelAsync();
            try
            {
                using var response = await request;
                Assert.AreNotEqual(HttpStatusCode.OK, response.StatusCode);
                Assert.DoesNotContain("PENDING-PRIVATE-PAGE", await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
            }
            catch (HttpRequestException)
            {
                // Closing an active tunnel may reach the caller as EOF instead of an HTTP error response.
            }

            Assert.IsNotEmpty(proxy.Failures);
        }
        finally { release.TrySetResult(); }
    }

    private static HttpClient Client(AiWebEgressProxy proxy)
    {
        // Authenticate each proxy connection, including automatic redirects, so destination grants remain the tested boundary.
        return new HttpClient(new SocketsHttpHandler
        {
            Proxy = new WebProxy(new Uri($"http://127.0.0.1:{proxy.Port}"))
            {
                BypassProxyOnLocal = false,
                Credentials = new NetworkCredential(proxy.Username, proxy.Password)
            },
            UseProxy = true,
            AllowAutoRedirect = true
        });
    }

    /// <summary>Gets or sets the cooperative deadline for owned HTTP requests and proxy cleanup.</summary>
    public TestContext TestContext { get; set; }
}