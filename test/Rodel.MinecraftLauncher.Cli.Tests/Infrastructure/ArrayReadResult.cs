using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>
/// Carries reconstructed values and observed protocol operations so cases can prove which navigation paths ran.
/// </summary>
internal sealed record ArrayReadResult(JsonArray Items, int PageCount, int ReferenceCount);