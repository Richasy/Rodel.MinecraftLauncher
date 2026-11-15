using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects the account credential envelope across actual current-user DPAPI and file storage.</summary>
[TestClass]
public sealed class ProtectedStoreTests
{
    /// <summary>
    /// AI credential tests cover a different binary store. This round trip exercises the account JSON envelope,
    /// native blob lengths and output lifetime; a damaged authentication tag must fail, not return plaintext or stale data.
    /// </summary>
    [TestMethod]
    public async Task AccountEnvelope_RoundTrips_AndRejectsDamagedCiphertext()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var file = Path.Combine(paths.Root, "synthetic-credential.json");
            var original = new JsonObject { ["token"] = "synthetic-not-a-player-token", ["future"] = new JsonArray(1, null, "value") };
            await ProtectedStore.WriteAsync(file, original, TestContext.CancellationToken);
            Assert.IsTrue(JsonNode.DeepEquals(original, ProtectedStore.Read(file)));
            var envelope = JsonFiles.ReadObject(file);
            Assert.IsFalse(envelope.ContainsKey("token"));
            var encrypted = Convert.FromBase64String(envelope["protected"]!.GetValue<string>());
            encrypted[^1] ^= 1;
            envelope["protected"] = Convert.ToBase64String(encrypted);
            await JsonFiles.WriteAsync(file, envelope, TestContext.CancellationToken);
            Assert.Throws<CryptographicException>(() => ProtectedStore.Read(file));
        });
    }

    /// <summary>Gets the runner deadline for isolated encrypted file I/O.</summary>
    public TestContext TestContext { get; set; } = null!;
}