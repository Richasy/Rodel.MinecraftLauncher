namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Explicit live prerequisites; merely having an API key must never trigger paid requests or load personal weights.</summary>
internal sealed record LiveOnlineSettings(string Protocol, string Endpoint, string Model, string CredentialEnvironment)
{
    /// <summary>Requires explicit live execution and a caller-selected existing local model reference; never downloads or auto-selects weights.</summary>
    public static string LocalModel()
    {
        RequireOptIn();
        return Required("RODEL_TEST_LOCAL_MODEL");
    }

    /// <summary>Reads the caller's actual online binding; a missing dependency is reported, never replaced with a mock response.</summary>
    public static LiveOnlineSettings Online()
    {
        RequireOptIn();
        var credential = Required("RODEL_TEST_ONLINE_CREDENTIAL_ENV");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(credential)))
            Assert.Inconclusive("The explicitly selected credential environment variable is absent.");
        return new LiveOnlineSettings(Required("RODEL_TEST_ONLINE_PROTOCOL"), Required("RODEL_TEST_ONLINE_ENDPOINT"),
            Required("RODEL_TEST_ONLINE_MODEL"), credential);
    }

    private static void RequireOptIn()
    {
        if (Environment.GetEnvironmentVariable("RODEL_TEST_LIVE") != "1")
            Assert.Inconclusive("Live tests are disabled. Set RODEL_TEST_LIVE=1 and explicitly select a LiveLocal, LiveLocalTools, LiveOnline or LiveMixed category.");
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) Assert.Inconclusive("Live prerequisite is missing: " + name);
        return value!;
    }
}