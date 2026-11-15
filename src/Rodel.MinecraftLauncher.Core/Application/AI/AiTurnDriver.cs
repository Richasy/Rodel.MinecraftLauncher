// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Assistant;
using System.Diagnostics;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Uses MEAI's function loop; Core callbacks retain only model policy, authorization and durable effects.</summary>
internal sealed class AiTurnDriver : FunctionInvokingChatClient
{
    private readonly AiProviderContext _snapshot;

    /// <summary>Connects one turn's provider policy to the official serial function driver.</summary>
    public AiTurnDriver(AiProviderContext snapshot,
        Func<IList<ChatMessage>, ChatOptions, CancellationToken, Task<ChatResponse>> model)
        : base(new ModelClient(model))
    {
        _snapshot = snapshot;
        AllowConcurrentInvocation = false;
        MaximumConsecutiveErrorsPerRequest = 0;
        MaximumIterationsPerRequest = int.MaxValue;
        TerminateOnUnknownCalls = false;
        IncludeDetailedErrors = false;
    }

    /// <summary>Runs the official loop once, without exposing tool descriptions or storage exception text to ambient agent telemetry.</summary>
    public async Task<ChatResponse> RunAsync(IReadOnlyList<ChatMessage> history, IReadOnlyList<AIFunctionDeclaration> declarations,
        Func<FunctionCallContent, CancellationToken, Task<FunctionResultContent>> invoke, CancellationToken ct)
    {
        var parent = Activity.Current;
        try
        {
            Activity.Current = null;
            return await GetResponseAsync(history, new ChatOptions
            {
                Tools = [.. declarations.Select(declaration => Guard(declaration, invoke))]
            }, ct).ConfigureAwait(false);
        }
        finally { Activity.Current = parent; }
    }

    /// <summary>Refreshes official-loop invokers from the same definitions used by the next model request.</summary>
    public static void SetTools(ChatOptions options, IReadOnlyList<AIFunctionDeclaration> declarations,
        Func<FunctionCallContent, CancellationToken, Task<FunctionResultContent>> invoke)
        => options.Tools = [.. declarations.Select(declaration => Guard(declaration, invoke))];

    /// <summary>Routes unoffered calls to the same explicit Core rejection/audit path, without advertising them to the provider.</summary>
    public static void RegisterRejectedCalls(ChatOptions options, ChatResponse response,
        Func<FunctionCallContent, CancellationToken, Task<FunctionResultContent>> invoke)
    {
        options.Tools ??= [];
        foreach (var call in response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>())
        {
            if (options.Tools.Any(tool => tool.Name == call.Name)) continue;
            options.Tools.Add(Guard(AiToolDeclarations.Create(call.Name, string.Empty, new() { ["type"] = "object" }), invoke));
        }
    }

    /// <inheritdoc />
    protected override IList<ChatMessage> CreateResponseMessages(ReadOnlySpan<FunctionInvocationResult> results)
    {
        var messages = new List<ChatMessage>();
        foreach (var batch in base.CreateResponseMessages(results))
        {
            foreach (var content in batch.Contents.OfType<FunctionResultContent>())
            {
                var message = AiChatMessages.ToolMessage(content);
                AiChatMessages.Bind(message, new(_snapshot.Protocol, _snapshot.ModelId, _snapshot.ServiceRevision, new System.Text.Json.Nodes.JsonObject()));
                messages.Add(message);
            }
        }

        return messages;
    }

    private static GuardedFunction Guard(AIFunctionDeclaration declaration,
        Func<FunctionCallContent, CancellationToken, Task<FunctionResultContent>> invoke) => new(declaration, invoke);

    private sealed class GuardedFunction(AIFunctionDeclaration declaration,
        Func<FunctionCallContent, CancellationToken, Task<FunctionResultContent>> invoke) : AIFunction
    {
        /// <inheritdoc />
        public override string Name => declaration.Name;
        /// <inheritdoc />
        public override string Description => string.Empty;
        /// <inheritdoc />
        public override JsonElement JsonSchema => declaration.JsonSchema;
        /// <inheritdoc />
        public override JsonSerializerOptions JsonSerializerOptions => AIJsonUtilities.DefaultOptions;
        /// <inheritdoc />
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            var context = CurrentContext ?? throw new InvalidOperationException("A guarded tool requires the MEAI turn invocation context.");
            return await invoke(context.CallContent, cancellationToken).ConfigureAwait(false);
        }
    }

    // Per-model work is streamed to Core's durable event observer; MEAI drives the enclosing tool loop through GetResponseAsync.
    private sealed class ModelClient(Func<IList<ChatMessage>, ChatOptions, CancellationToken, Task<ChatResponse>> model) : IChatClient
    {
        /// <inheritdoc />
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (messages is not IList<ChatMessage> { IsReadOnly: false } working)
                throw new InvalidOperationException("The MEAI turn policy requires its mutable working history.");
            return model(working, options ?? new(), cancellationToken);
        }
        /// <inheritdoc />
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Use the turn driver; per-model streaming is published through Core events.");
        /// <inheritdoc />
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        /// <inheritdoc />
        public void Dispose() { }
    }
}