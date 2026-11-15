using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>
/// Checks process status, output channels, and the JSON envelope; individual cases assert business outcomes.
/// </summary>
internal sealed record CliResult(int ExitCode, string Output, string Error)
{
    internal JsonObject ReadEnvelope(int expectedExitCode)
    {
        Assert.AreEqual(expectedExitCode, ExitCode, $"stdout: {Output}{Environment.NewLine}stderr: {Error}");
        Assert.AreEqual(string.Empty, Error, Error);
        return JsonNode.Parse(Output)?.AsObject() ?? throw new AssertFailedException("CLI did not return a JSON envelope.");
    }
}