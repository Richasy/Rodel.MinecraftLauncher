// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Graphics;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

internal enum AiConversationAction
{
    Rename,
    Export,
    Remove,
}

/// <summary>Edits or exports one captured conversation, preserving Core's removal review and write boundary.</summary>
internal sealed partial class AiConversationDialog : Control
{
    private readonly AiUiState _state;
    private readonly Action _close;
    private readonly AiConversationFields _fields;
    private AiConversationRemovalReview? _removal;
    private bool _disposed;
    private bool _overwrite;

    internal AiConversationDialog(
        AiUiState state,
        Window window,
        AiConversationAction action,
        AiUiConversation conversation,
        Action close)
    {
        _state = state;
        Window = window;
        Action = action;
        Conversation = conversation;
        _close = close;
        _fields = new(this);
        Name = conversation.Title;
        Path = string.Empty;
        Error = string.Empty;
        Preview = string.Empty;
    }

    internal Window Window { get; }
    internal AiConversationAction Action { get; }
    internal AiUiConversation Conversation { get; }
    internal partial string Name { get; set; }
    internal partial string Path { get; private set; }
    internal partial string Error { get; private set; }
    internal partial string Preview { get; private set; }
    internal partial bool Working { get; private set; }

    internal void ChooseExportPath(string path)
    {
        Path = path;
        _overwrite = true;
    }

    private string Title => Action switch
    {
        AiConversationAction.Rename => Resources.AiRenameChat,
        AiConversationAction.Export => Resources.AiExport,
        AiConversationAction.Remove => Resources.AiDiscardDraft,
        _ => throw new InvalidOperationException("Unknown conversation action."),
    };
    private string SubmitLabel => Action switch
    {
        AiConversationAction.Remove => Resources.AiRemove,
        AiConversationAction.Export => Resources.AiExport,
        _ => Resources.AiSave,
    };
    private bool CanSubmit => !Working && (Action switch
    {
        AiConversationAction.Rename => Name.Trim().Length > 0,
        AiConversationAction.Export => Conversation.Persisted && Path.Trim().Length > 0,
        AiConversationAction.Remove => !Conversation.Persisted || _removal is not null,
        _ => false,
    });

    private Ui Body => new AiDialogFrame(
LauncherAppearance.Capture(), AiDialogAppearance.Capture(),
        Title,
        _fields,
        _close,
        SubmitLabel,
        () => _ = SubmitAsync(),
        CanSubmit,
        Math.Min(Action == AiConversationAction.Rename ? 300f : 440f, Window.ClientSize.Height - 64f));

    private async Task SubmitAsync()
    {
        if (!CanSubmit)
            return;
        Working = true;
        var saved = await _state.RunAsync(async () =>
            {
                switch (Action)
                {
                    case AiConversationAction.Rename:
                        await _state.Chats.RenameAsync(Conversation, Name.Trim());
                        break;
                    case AiConversationAction.Remove:
                        await _state.Chats.RemoveAsync(Conversation, _removal);
                        break;
                    case AiConversationAction.Export:
                        await _state.Service.ExportConversationAsync(new()
                        {
                            ConversationId = Conversation.Id,
                            Destination = Path.Trim(),
                            Overwrite = _overwrite,
                        });
                        break;
                }
            });
        if (_disposed)
            return;
        Error = Action == AiConversationAction.Rename ? string.Empty : _state.Error;
        Working = false;
        if (saved)
            _close();
        else if (Action == AiConversationAction.Rename)
            LauncherToast.ShowError(Window, _state.Error);
    }

    private async Task ReviewRemovalAsync()
    {
        if (!Conversation.Persisted)
        {
            Preview = Resources.AiRemoveDraftNotice;
            return;
        }

        Working = true;
        try
        {
            var id = Conversation.Id;
            var review = await Task.Run(() => _state.Service.ReviewConversationRemovalAsync(id));
            if (_disposed)
                return;
            _removal = review;
            Preview = string.Format(Resources.AiRemoveReview, Conversation.Title, review.TurnIds.Count, review.IndependentJobIds.Length);
        }
        catch (Exception error)
        {
            if (!_disposed)
                Error = error.Message;
        }
        finally
        {
            if (!_disposed)
                Working = false;
        }
    }

    protected override IDisposable? OnMounted()
    {
        if (Action == AiConversationAction.Remove)
            _ = ReviewRemovalAsync();
        return null;
    }

    protected override void OnDispose()
    {
        _disposed = true;
        base.OnDispose();
    }
}

internal sealed partial class AiConversationFields(AiConversationDialog owner) : Control
{
    private Ui Body => Column([
        Text(owner.Error)
            .FontSize(12f).Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(owner.Error.Length > 0),
        Text(owner.Preview)
            .FontSize(13f).Foreground(LauncherTheme.Ink)
            .Wrap()
            .Visible(owner.Action == AiConversationAction.Remove),
        Text(Resources.AiExportDestination)
            .FontSize(13f).Foreground(LauncherTheme.Ink)
            .Visible(owner.Action == AiConversationAction.Export),
        new FileSystemPathInput(
            owner.Window,
            owner.Path,
            value => owner.ChooseExportPath(value),
            Resources.AiExportDestination,
            "ai.export.path",
            Enabled: !owner.Working,
            Extensions: [".json"],
            SaveName: "conversation").Visible(owner.Action == AiConversationAction.Export),
        Text(Resources.AiChatName)
            .FontSize(13f).Foreground(LauncherTheme.Ink)
            .Visible(owner.Action == AiConversationAction.Rename),
        TextBox(owner.Name, value => owner.Name = value)
            .Style(LauncherStyles.Input)
            .Visible(owner.Action == AiConversationAction.Rename)
            .Automation(new()
            {
                Name = Resources.AiDisplayName,
                AutomationId = "ai.entry.name"
            }),
    ])
        .Spacing(12f);
}