using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>
/// Runs the real stdio host through the official SDK; handles transport and envelope consistency, not business expectations.
/// </summary>
internal sealed class McpSession(McpClient client, ConcurrentQueue<string> errors) : IAsyncDisposable
{
    internal static async Task<McpSession> ConnectAsync(CliSandbox cli, string profile = "launcher", string? skinScope = null)
    {
        var start = cli.CreateStartInfo("mcp", "serve", "--stdio", "--tool-profile", profile);
        if (skinScope is not null)
        {
            start.ArgumentList.Add("--skin-scope");
            start.ArgumentList.Add(skinScope);
        }

        return await ConnectAsync(start);
    }

    internal static async Task<McpSession> ConnectAsync(ProcessStartInfo start)
    {
        var errors = new ConcurrentQueue<string>();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = start.FileName,
            Arguments = [.. start.ArgumentList],
            WorkingDirectory = start.WorkingDirectory,
            EnvironmentVariables = new Dictionary<string, string?> { ["RODEL_CLI_LANGUAGE"] = "en" },
            StandardErrorLines = errors.Enqueue,
            ShutdownTimeout = TimeSpan.FromSeconds(5)
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return new(await McpClient.CreateAsync(transport, cancellationToken: deadline.Token), errors);
    }

    internal async Task<string[]> ToolNamesAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return [.. (await client.ListToolsAsync(cancellationToken: deadline.Token)).Select(tool => tool.Name)];
    }

    internal async Task<JsonElement> ToolSchemaAsync(string name)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return (await client.ListToolsAsync(cancellationToken: deadline.Token)).Single(tool => tool.Name == name).JsonSchema.Clone();
    }

    internal async Task<byte[]> PreviewAsync(JsonObject arguments, string toolName = "skin_preview")
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await client.CallToolAsync(new CallToolRequestParams
        {
            Name = toolName,
            Arguments = arguments.ToDictionary(entry => entry.Key, entry => JsonSerializer.SerializeToElement(entry.Value))
        }, deadline.Token);
        Assert.AreNotEqual(true, result.IsError, string.Join(Environment.NewLine, errors));
        return result.Content.OfType<ImageContentBlock>().Single().DecodedData.ToArray();
    }

    internal async Task<JsonObject> CallAsync(string name, JsonObject arguments)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await client.CallToolAsync(new CallToolRequestParams
        {
            Name = name,
            Arguments = arguments.ToDictionary(entry => entry.Key, entry => JsonSerializer.SerializeToElement(entry.Value))
        }, deadline.Token);

        Assert.IsNotNull(result.StructuredContent, string.Join(Environment.NewLine, errors));
        var envelope = JsonNode.Parse(result.StructuredContent.Value.GetRawText())!.AsObject();
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        Assert.IsTrue(JsonNode.DeepEquals(envelope, JsonNode.Parse(text)), "Text and structured MCP results diverged.");
        Assert.AreEqual(envelope["status"]?.GetValue<string>() != "succeeded", result.IsError == true, "MCP error flag disagrees with the result envelope.");
        return envelope;
    }

    /// <summary>Disposes the SDK client and awaits stdio transport completion and process exit before sandbox removal.</summary>
    /// <returns>A task completing after the owned MCP process releases its working directory, or a cleanup timeout.</returns>
    public async ValueTask DisposeAsync()
    {
        await client.DisposeAsync();
        var completion = await client.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        if (completion is not StdioClientCompletionDetails { ProcessId: { } processId })
            return;

        // SDK disposal may request termination before Windows releases the working directory.
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (process)
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            await process.WaitForExitAsync(deadline.Token);
    }
}