using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects the real file-handle ownership boundary, including release without deleting the shared rendezvous.</summary>
[TestClass]
public sealed class ResourceLeaseTests
{
    /// <summary>
    /// Review: protocol tests only encounter delete-pending races probabilistically. Keep the real rendezvous
    /// alive across release and reacquisition, while proving that its existence alone does not represent ownership.
    /// Cancellation is requested after AcquireAsync has synchronously attempted the held file, without timing sleeps.
    /// </summary>
    [TestMethod]
    public async Task Release_RetainsRendezvous_AndNextOwnerStillExcludesContenders()
    {
        var resource = Path.Combine(Path.GetTempPath(), "rodel-lease-test-" + Guid.NewGuid().ToString("N"));
        var key = ResourceLease.CanonicalPath(resource).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        var file = Path.Combine(Path.GetTempPath(), "Rodel.MinecraftLauncher.Locks", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".lock");
        try
        {
            using (await ResourceLease.AcquireAsync(resource, TestContext.CancellationToken))
            {
                await AssertContenderWaitsAsync(resource);
            }

            Assert.IsTrue(File.Exists(file), "Release must not put the shared path into Windows delete-pending state.");
            Assert.AreEqual(0L, new FileInfo(file).Length);
            using (await ResourceLease.AcquireAsync(resource, TestContext.CancellationToken))
            {
                await AssertContenderWaitsAsync(resource);
            }
        }
        finally
        {
            // This unique test resource has no remaining owners or waiters.
            File.Delete(file);
        }
    }

    private static async Task AssertContenderWaitsAsync(string resource)
    {
        using var cancellation = new CancellationTokenSource();
        var pending = ResourceLease.AcquireAsync(resource, cancellation.Token);
        Assert.IsFalse(pending.IsCompleted, "A contender cannot enter while the exclusive handle is held.");
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);
    }

    /// <summary>Gets or sets the runner's cancellation signal.</summary>
    public TestContext TestContext { get; set; } = null!;
}