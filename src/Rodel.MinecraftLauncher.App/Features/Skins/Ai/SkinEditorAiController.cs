// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Ai;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout.Reactive;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Binds the shared AI pipeline to the current skin and refreshes committed edits without frontend approval steps.</summary>
internal sealed class SkinEditorAiController : IDisposable
{
    private readonly LauncherClient _client;
    private readonly SkinEditorState _editor;
    private readonly AiConfigurationStore _configuration;
    private readonly Dictionary<string, SkinAiWorkspace> _projects = [];
    private readonly Signal<bool> _editing = new(false);
    private bool _disposed;
    private readonly CancellationTokenSource _lifetime = new();

    internal SkinEditorAiController(LauncherClient client, SkinEditorState editor)
    {
        _client = client;
        _editor = editor;
        _configuration = new(client);
        editor.Controller.Changed += Synchronize;
        editor.Controller.Window.Activated += OnActivated;
    }

    internal SkinAiWorkspace? Current { get; private set; }
    internal bool IsEditing => _editing.Value;
    internal event Action? Changed;

    private void Notify()
    {
        _editing.Value = Current is { } workspace && (workspace.Preparing || workspace.Refreshing || workspace.ReloadPending || workspace.Ui.Current.Active);
        Changed?.Invoke();
    }

    private void Synchronize()
    {
        if (_disposed || _editor.Controller.Session is not { } session)
            return;
        if (!_projects.TryGetValue(session.Id, out var workspace))
        {
            workspace = new(session.Id, new(_client, _configuration));
            _projects.Add(session.Id, workspace);
            workspace.Ui.Changed += () => Observe(workspace);
            Current = workspace;
            _ = LoadAsync(workspace);
        }
        else
        {
            Current = workspace;
        }

        if (workspace.ReloadPending && !workspace.Refreshing)
            _ = ReloadDocumentAsync(workspace);
        Notify();
    }

    private async Task LoadAsync(SkinAiWorkspace workspace)
    {
        var cancellation = _lifetime.Token;
        workspace.Preparing = true;
        Notify();
        try
        {
            await workspace.Ui.Configuration.LoadAsync();
            cancellation.ThrowIfCancellationRequested();
            var offset = 0;
            while (true)
            {
                var (Count, HasMore, Id) = await Task.Run(async () =>
                    {
                        var records = await workspace.Ui.Service.ListConversationsAsync(offset, 100, ct: cancellation).ConfigureAwait(false);
                        string? match = null;
                        foreach (var record in records.Items.Where(item => item.SkinScopeId is not null))
                        {
                            cancellation.ThrowIfCancellationRequested();
                            var scope = _client.Skins.Proposals.ReadScope(record.SkinScopeId!);
                            if (scope.DocumentId == workspace.DocumentId && scope.Limits.DirectEditing && !scope.Revoked)
                            {
                                match = record.Id;
                                break;
                            }
                        }

                        return (Count: records.Items.Length, records.HasMore, Id: match);
                    }, cancellation);
                cancellation.ThrowIfCancellationRequested();
                if (Id is { } id)
                {
                    await workspace.Ui.Chats.LoadConversationAsync(id);
                    break;
                }

                offset += Count;
                if (!HasMore || Count == 0)
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!_disposed)
                workspace.Error = error.Message;
        }
        finally
        {
            if (!_disposed)
            {
                workspace.Preparing = false;
                Observe(workspace);
            }
        }
    }

    internal async Task SendAsync(AiUiTurn? retry = null)
    {
        if (Current is not { } workspace || workspace.Preparing || workspace.Ui.Busy || !_editor.Controller.LayerActions.CanSubmitAi || _editor.Controller.Input.HasGesture || _editor.Controller.Session is not { } session)
        {
            return;
        }

        workspace.Preparing = true;
        workspace.Error = string.Empty;
        Notify();
        try
        {
            var conversation = workspace.Ui.Current;
            if (conversation.Submission is null)
            {
                var cancellation = _lifetime.Token;
                var existing = conversation.SkinScopeId is { } scopeId ? await Task.Run(() => _client.Skins.Proposals.ReadScope(scopeId), cancellation) : null;
                cancellation.ThrowIfCancellationRequested();
                if (existing is null || existing.Model != session.Render().Model || existing.Size != session.Render().Size || existing.Revoked)
                {
                    if (retry is not null)
                        throw new InvalidOperationException(Resources.SkinAiScopeChanged);
                    var scope = await Task.Run(() => _client.Skins.Proposals.CreateScopeAsync(session.Id, new(DirectEditing: true)), cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    var next = new AiUiConversation
                    {
                        SkinScopeId = scope.ScopeId,
                        ModelId = conversation.ModelId ?? workspace.Ui.Configuration.Defaults.ModelId,
                        Draft = conversation.Draft,
                        Files = false,
                        Network = false
                    };
                    next.Images.AddRange(conversation.Images);
                    workspace.Ui.Conversations.Add(next);
                    workspace.Ui.Select(next);
                    conversation = next;
                }
            }

            await workspace.Ui.Chats.SendAsync(conversation, retry);
        }
        catch (Exception error)
        {
            if (!_disposed)
                workspace.Error = error.Message;
        }
        finally
        {
            if (!_disposed)
            {
                workspace.Preparing = false;
                Observe(workspace);
            }
        }
    }

    private void Observe(SkinAiWorkspace workspace)
    {
        if (_disposed)
            return;
        Notify();
        var changes = workspace.Ui.Current.Turns.Select(turn => (Turn: turn, Write: turn.Events.LastOrDefault(item => item.Kind == "tool_finished" && item.Data?["name"]?.ToString() == "skin_propose"))).Where(item => item.Write is not null || item.Turn.ObservedTerminal).ToArray();
        var key = string.Join("|", changes.Select(item => item.Turn.Id + ":" + item.Write?.Sequence + ":" + item.Turn.ObservedTerminal));
        if (changes.Length > 0 && key != workspace.ReloadKey)
        {
            workspace.ReloadKey = key;
            workspace.ReloadPending = true;
        }

        if (workspace.ReloadPending && !workspace.Refreshing)
            _ = ReloadDocumentAsync(workspace);
    }

    private async Task ReloadDocumentAsync(SkinAiWorkspace workspace)
    {
        if (workspace.Refreshing)
            return;
        workspace.Refreshing = true;
        Notify();
        try
        {
            while (workspace.ReloadPending)
            {
                workspace.ReloadPending = false;
                var fresh = await Task.Run(() => _client.Skins.OpenEditor(workspace.DocumentId));
                if (_disposed)
                    return;
                if (_editor.Controller.Session is { } session && session.Id == workspace.DocumentId && session.Revision != fresh.Revision)
                {
                    if (session.HasDraft || _editor.Busy || _editor.HasLayerDraft || _editor.Controller.HasDeferredStroke)
                    {
                        workspace.ReloadPending = true;
                        break;
                    }

                    if (fresh.Layers.Any(layer => layer.Id == session.ActiveLayerId))
                        fresh.SelectLayer(session.ActiveLayerId!);
                    _editor.Controller.Adopt(fresh, _editor.ProjectName);
                    _editor.Status = Resources.SkinSaved;
                }
            }
        }
        catch (Exception error)
        {
            if (!_disposed)
                workspace.Error = error.Message;
        }
        finally
        {
            if (!_disposed)
            {
                workspace.Refreshing = false;
                Notify();
            }
        }
    }

    private void OnActivated(object? sender, EventArgs args)
    {
        if (!_disposed)
            _ = _configuration.LoadAsync(force: true);
    }

    /// <summary>Releases only this window's observers; Core retains durable execution and history for reopening.</summary>
    public void Dispose()
    {
        _disposed = true;
        _editor.Controller.Changed -= Synchronize;
        _editor.Controller.Window.Activated -= OnActivated;
        _lifetime.Cancel();
        _lifetime.Dispose();
        foreach (var workspace in _projects.Values)
            workspace.Ui.Dispose();
        _configuration.Dispose();
    }
}