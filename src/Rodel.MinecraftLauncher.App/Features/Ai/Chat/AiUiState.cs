// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout.Reactive;
using System.Security.Cryptography;
using System.Text;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Retains UI drafts while Core owns durable configuration, conversations and execution.</summary>
internal sealed class AiUiState : IDisposable
{
    private readonly AiOperationGate _operations;
    private readonly bool _ownsConfiguration;
    private readonly Effect _defaultsEffect;
    private AiConversationDefaults? _appliedDefaults;
    private Task? _load;
    private bool _disposed;

    internal AiUiState(
        LauncherClient client,
        AiConfigurationStore? configuration = null,
        IDictionary<string, object>? readReceipts = null,
        AiOperationGate? operations = null)
    {
        _operations = operations ?? new();
        Activity = client.Activity;
        Service = client.Assistant;
        Repairs = client.Repairs;
        if (configuration is not null && !string.Equals(configuration.DataRoot, client.Paths.Root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("AI configuration belongs to a different launcher data root.", nameof(configuration));
        _ownsConfiguration = configuration is null;
        Configuration = configuration ?? new(client, _operations);
        Chats = new(this);
        _readPrefix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(client.Paths.Root)))[..24];
        _readReceipts = readReceipts ?? AppStorage.OpenSettings("AiReadReceipts");
        _defaultsEffect = new(ApplyDefaults);
    }
    private readonly IDictionary<string, object> _readReceipts;
    private readonly string _readPrefix;
    internal Func<bool>? IsReading { get; set; }

    internal void RestoreReadReceipt(AiUiConversation conversation)
    {
        if (_readReceipts.TryGetValue(_readPrefix + conversation.Id, out var value) && value is string receipt)
            conversation.ReadReceipt = receipt;
    }

    internal void MarkRead()
    {
        if (Loading || Current.HistoryLoading || Current.HistoryFailed)
            return;
        var receipt = Current.LatestReceipt;
        if (receipt is null || Current.ReadReceipt == receipt)
            return;
        try
        {
            _readReceipts[_readPrefix + Current.Id] = receipt;
            Current.ReadReceipt = receipt;
        }
        catch (Exception error)
        {
            Error = error.Message;
        }
    }

    internal LauncherActivityLog Activity { get; }
    internal AssistantService Service { get; }
    internal AiRepairService Repairs { get; }
    internal AiConfigurationStore Configuration { get; }
    internal AiChatController Chats { get; }
    internal bool Busy => _operations.Busy;
    internal bool Loaded { get; private set; }
    internal bool Loading => _load is not null;
    internal bool TranscriptActive { get; private set; } = true;
    internal string Error { get; private set; } = string.Empty;

    internal async Task<bool> RunAsync(Func<Task> action, bool waitForAvailability = false)
    {
        while (true)
        {
            if (_disposed)
                return false;
            if (_operations.TryEnter())
                break;
            if (!waitForAvailability)
                return false;
            await _operations.WhenAvailable;
        }

        Error = string.Empty;
        Notify();
        try
        {
            await action();
            return !_disposed;
        }
        catch (Exception error)
        {
            if (!_disposed)
                Error = error.Message;
            return false;
        }
        finally
        {
            _operations.Exit();
            Notify();
        }
    }

    internal void Report(Exception error)
    {
        Error = error.Message;
        Notify();
    }

    internal void ClearError() => Error = string.Empty;

    /// <summary>Initializes navigation and model settings without retaining every saved conversation's payloads.</summary>
    internal Task<bool> LoadConfigurationAsync() => Configuration.LoadAsync(force: true);

    internal Task LoadAsync()
    {
        if (_load is { } pending)
            return pending;
        var completion = new TaskCompletionSource();
        _load = completion.Task;
        _ = LoadCoreAsync(completion);
        return completion.Task;
    }

    private async Task LoadCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            await RunAsync(async () =>
            {
                if (!await Configuration.LoadAsync(force: true))
                    return;
                await Chats.LoadAsync();
                if (TranscriptActive && Current.Persisted)
                    await Chats.SelectAsync(Current);
                Loaded = true;
            }, waitForAvailability: true);
            _load = null;
            Notify();
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            _load = null;
            Notify();
            completion.TrySetException(error);
        }
    }
    internal event Action? Changed;
    internal List<AiUiConversation> Conversations { get; } = [];
    internal AiUiConversation Current { get; private set; } = new();

    internal void Notify()
    {
        if (_disposed)
            return;
        if (IsReading?.Invoke() == true)
            MarkRead();
        Changed?.Invoke();
    }

    internal void NewConversation()
    {
        Chats.ReleaseCompletedTranscript(Current);
        Current = CreateDraft();
        Conversations.Insert(0, Current);
        Notify();
    }

    internal void Select(AiUiConversation conversation)
    {
        if (!ReferenceEquals(Current, conversation))
            Chats.ReleaseCompletedTranscript(Current);
        Current = conversation;
        if (TranscriptActive)
            _ = Chats.SelectAsync(conversation);
        Notify();
    }

    internal void SetTranscriptActive(bool active)
    {
        if (TranscriptActive == active)
            return;
        TranscriptActive = active;
        if (!active)
            Chats.ReleaseCompletedTranscript(Current);
        else if (Current.Persisted)
            _ = Chats.SelectAsync(Current);
        Notify();
    }

    internal void Discard(AiUiConversation conversation)
    {
        Conversations.Remove(conversation);
        if (ReferenceEquals(Current, conversation))
        {
            Chats.ReleaseCompletedTranscript(conversation);
            Current = CreateDraft();
        }

        Notify();
    }

    private AiUiConversation CreateDraft() => new()
    {
        Files = Configuration.Defaults.Files,
        Network = Configuration.Defaults.Network,
        Operate = Configuration.Defaults.Operate,
        ModelId = Configuration.Defaults.ModelId
    };

    private void ApplyDefaults()
    {
        if (!Configuration.Loaded)
            return;
        var defaults = Configuration.Defaults;
        var previous = _appliedDefaults;
        _appliedDefaults = defaults;
        if (!Current.Persisted)
        {
            if (Current.SkinScopeId is null && previous is null)
            {
                Current.Files = defaults.Files;
                Current.Network = defaults.Network;
                Current.Operate = defaults.Operate;
            }

            if (Current.SkinScopeId is null && previous is not null && previous.Operate != defaults.Operate && !Current.ModeSelected && !Conversations.Contains(Current))
            {
                Current.Operate = defaults.Operate;
            }

            Current.ModelId ??= defaults.ModelId;
        }

        Untracked.Run(Notify);
    }

    /// <summary>Releases this UI's observers and only configuration it created, without stopping Core turns.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _defaultsEffect.Dispose();
        Chats.Dispose();
        if (_ownsConfiguration)
            Configuration.Dispose();
        Changed = null;
        IsReading = null;
    }
}

internal sealed class AiUiConversation
{
    internal string UiKey { get; } = Guid.NewGuid().ToString("N");
    internal string Id { get; set; } = Guid.NewGuid().ToString("N");
    internal bool Persisted { get; set; }
    internal bool Removing { get; set; }
    internal bool Removed { get; set; }
    internal bool HistoryLoading { get; set; }
    internal bool HistoryFailed { get; set; }
    internal int TranscriptVersion { get; set; }
    internal long Revision { get; set; }
    internal AiChatSend? Submission { get; set; }
    internal AiUiTurn? PendingTurn { get; set; }
    internal bool Sending { get; set; }
    internal bool StopRequested { get; set; }
    internal List<AiUiTurn> Turns { get; } = [];
    internal IEnumerable<AiUiTurn> DisplayTurns => PendingTurn is { } pending ? Turns.Append(pending) : Turns;
    internal List<AiUiContextChange> ContextChanges { get; } = [];
    internal bool Active => Sending || Turns.Any(turn => !AiChatStates.IsTerminal(turn.Record.Status));
    internal string? ReadReceipt { get; set; }
    internal AiUiTurn? LatestFinished => Turns.Where(turn => AiChatStates.IsTerminal(turn.Record.Status)).OrderByDescending(turn => turn.Record.UpdatedUtc, StringComparer.Ordinal).FirstOrDefault();
    internal string? LatestReceipt => LatestFinished is { } latest ? latest.Id + "|" + latest.Record.UpdatedUtc : null;
    internal bool Responding => Sending || Turns.Any(turn => turn.Record.Status is "connecting" or "generating" or "canceling");
    internal bool NeedsInput => Turns.Any(turn => turn.Record.Status is "awaiting_user" or "awaiting_confirmation" or "awaiting_access");
    internal string Name { get; set; } = string.Empty;
    internal string Draft { get; set; } = string.Empty;
    internal List<AiChatImage> Images { get; } = [];
    internal System.Numerics.Vector2? TranscriptOffset { get; set; }
    internal string? InstanceId { get; set; }
    internal string? SkinScopeId { get; set; }
    internal string? RepairId { get; set; }
    internal AiRepairCreate? RepairRequest { get; set; }
    internal AiRepairReport? Repair { get; set; }
    internal string? ModelId { get; set; }
    internal bool Operate { get; set; }
    internal bool ModeSelected { get; set; }
    internal string StoredMode { get; set; } = string.Empty;
    internal bool Files { get; set; } = true;
    internal bool Network { get; set; } = true;
    internal string Title => Name.Length == 0 ? Resources.AiNewChat : Name;
}

internal sealed record AiUiContextChange(
    string Id,
    string AfterTurnId,
    string Kind,
    string? Value);

internal sealed class AiUiTurn(AiChatRecord record)
{
    internal AiChatRecord Record { get; set; } = record;
    internal AiApproval? Approval { get; set; }
    internal AiUserAnswer? UserAnswer { get; set; }
    internal HashSet<string> ExpandedParts { get; } = [with(StringComparer.Ordinal)];
    internal string Id => Record.Id!;
    internal bool ObservedTerminal { get; set; }
    internal long Cursor { get; set; }
    internal string Text { get; set; } = string.Empty;
    internal List<AiChatEvent> Events { get; } = [];
    internal bool Details { get; set; }
    internal bool OutputLimited => Record.Error?["code"]?.ToString() == "AI_OUTPUT_TOKEN_LIMIT" || (Record.FinishReason ?? Events.LastOrDefault(item => item.Kind == "model_finished")?.Data?["finishReason"]?.ToString()) is "max_output_tokens" or "length" or "max_tokens" or "MAX_TOKENS";
    private int _contentCount = -1;
    private string _contentAnswer = string.Empty;
    private bool _contentTerminal;
    internal void ReleaseTranscript()
    {
        ObservedTerminal = false;
        Cursor = 0;
        Text = string.Empty;
        Events.Clear();
        Parts = [];
        _contentCount = -1;
        _contentAnswer = string.Empty;
    }

    internal AiTranscriptPart[] Parts
    {
        get
        {
            var answer = Record.Answer ?? Text;
            var terminal = AiChatStates.IsTerminal(Record.Status);
            if (_contentCount != Events.Count || _contentAnswer != answer || _contentTerminal != terminal)
            {
                field = AiTranscriptContent.Read(Events, answer, terminal);
                _contentCount = Events.Count;
                _contentAnswer = answer;
                _contentTerminal = terminal;
            }

            return field;
        }
        private set;
    } = [];
}