// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Assistant;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed class LauncherOperationException(string code, string message, JsonNode? details = null) : Exception(message)
{
    public string Code { get; } = code;
    public JsonNode? Details { get; } = details;

    public static JsonObject ToResult(Exception exception) =>
        JsonSerializer.SerializeToNode(CreateResult(exception), LauncherOperationJsonContext.Default.LauncherOperationResult)!.AsObject();

    public static LauncherOperationResult CreateResult(Exception exception) => new(
        1,
        exception is OperationCanceledException ? LauncherResultStatus.Canceled : LauncherResultStatus.Failed,
        (exception is LauncherOperationCanceledException canceled ? canceled.Details : (exception as LauncherOperationException)?.Details ?? (exception as AiProtocolException)?.Details)?.DeepClone(),
        new LauncherOperationError(
            exception switch
            {
                LauncherOperationException operation => operation.Code,
                AiProtocolException protocol => protocol.Code,
                Rodel.MinecraftLauncher.Core.Content.ContentPlatformUnavailableException => "PLATFORM_UNAVAILABLE",
                OperationCanceledException => "CANCELED",
                ArgumentException => "INVALID_ARGUMENT",
                FileNotFoundException or DirectoryNotFoundException => "FILE_NOT_FOUND",
                HttpRequestException => "NETWORK_ERROR",
                InvalidDataException or System.Text.Json.JsonException => "INVALID_DATA",
                IOException => "IO_ERROR",
                _ => "OPERATION_FAILED"
            },
            exception.Message,
            exception is HttpRequestException network ? network.HttpRequestError.ToString() : null,
            exception is HttpRequestException { StatusCode: { } status } ? (int)status : null));
}

/// <summary>A canceled operation that retained published data and needs an explicit recovery target.</summary>
public sealed class LauncherOperationCanceledException(string message, JsonNode details, OperationCanceledException inner)
    : OperationCanceledException(message, inner, inner.CancellationToken)
{
    public JsonNode Details { get; } = details;
}