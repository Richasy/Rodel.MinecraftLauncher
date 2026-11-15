// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>In-memory launch credentials read from protected storage; this type is deliberately not a serializable report.</summary>
internal sealed class GameLaunchCredentials
{
    internal string MinecraftToken { get; }
    internal string ClientId { get; }
    internal string Xuid { get; }
    internal JsonNode? AuthenticationMetadata { get; }

    private GameLaunchCredentials(string minecraftToken, string clientId, string xuid, JsonNode? authenticationMetadata)
        => (MinecraftToken, ClientId, Xuid, AuthenticationMetadata) = (minecraftToken, clientId, xuid, authenticationMetadata);

    internal static GameLaunchCredentials Read(JsonObject document) => new(
        document["minecraftToken"]?.GetValue<string>() ?? throw new InvalidDataException("The protected session has no Minecraft token."),
        document["clientId"]?.GetValue<string>() ?? throw new InvalidDataException("The protected session has no client identifier."),
        document["xuid"]?.GetValue<string>() ?? string.Empty,
        document["metadata"]?.DeepClone());
}