// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Ai;
using Rodel.MinecraftLauncher.App.Features.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Popups;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Retains streaming chat and its model, reference, replacement and send/stop composer actions.</summary>
internal sealed partial class SkinEditorAiPanel : Control
{
    private readonly SkinEditorState _editor;
    private readonly ScrollView _transcript;
    private readonly AiTranscriptScroll _scroll;
    private readonly SkinAiPrompt _prompt;
    private partial bool AddingImages { get; set; }
    private partial string ReferenceError { get; set; }
    private bool _disposed;

    internal SkinEditorAiPanel(SkinEditorState editor)
    {
        _editor = editor;
        _transcript = new(new SkinAiTranscript(this));
        _scroll = new(_transcript, editor.Controller.Window, FindAnchor);
        _prompt = new(this);
    }

    internal partial int Revision { get; private set; }
    private SkinAiWorkspace? Workspace
    {
        get
        {
            _ = Revision;
            return _editor.Ai.Current;
        }
    }

    internal AiUiConversation? Conversation => Workspace?.Ui.Current;
    internal AiUiTurn[] Turns => Conversation is { } conversation ? [.. conversation.Turns] : [];

    internal string LayerName(string? id) => _editor.Layers.FirstOrDefault(layer => layer.Id == (id ?? SkinDocument.BackgroundLayerId))?.Name ?? (id is null or SkinDocument.BackgroundLayerId ? Resources.SkinBackgroundLayer : id);

    internal bool HasContinuation(AiUiTurn turn) => SkinAiActivity.HasContinuation(turn.Record, Turns.Select(item => item.Record));

    internal bool ShowRetry(AiUiTurn turn) => ReferenceEquals(Conversation?.Turns.LastOrDefault(), turn) && AiChatStates.IsTerminal(turn.Record.Status) && turn.Record.Status != "completed" && turn.Record.UnknownEffects is not { Count: > 0 } && !HasContinuation(turn);
    private AiUiModel[] Models => Workspace is { } workspace ? [.. workspace.Ui.Configuration.Providers.Where(provider => provider.Enabled).SelectMany(provider => provider.Models)] : [];
    internal bool Busy => AddingImages || Workspace?.Preparing == true || Workspace?.Ui.Busy == true;
    private bool Active => Conversation?.Active == true;
    private bool Pending => Conversation?.Submission is not null;
    private bool HasConversation => Conversation?.DisplayTurns.Any() == true;
    private int ComposerActions => 2 + (HasConversation ? 1 : 0) + (Active ? 1 : 0);
    private bool CanSend => !Busy && Models.Length > 0 && (Pending || !string.IsNullOrWhiteSpace(Conversation?.Draft) || DraftImages.Length > 0) && _editor.Controller.LayerActions.CanSubmitAi;
    private int ModelIndex => Array.FindIndex(Models, model => model.Id == (Conversation?.ModelId ?? Workspace?.Ui.Configuration.Defaults.ModelId));
    internal string Error => Workspace is { } workspace ? string.Join(Environment.NewLine, new[] { workspace.Error, workspace.Ui.Error, workspace.Ui.Configuration.Error, ReferenceError ?? string.Empty }.Where(text => text.Length > 0)) : ReferenceError ?? string.Empty;
    private AiChatImage[] DraftImages => Conversation is { } conversation ? [.. conversation.Images] : [];

    internal byte[] ReadImage(string id) => Workspace!.Ui.Service.Images.ReadBytes(id);
    private bool CanAddImages => !Busy && !Pending && Conversation is not null && DraftImages.Length < AiImageStore.MaximumImages;
    private bool Empty => Turns.Length == 0 && !Busy && Error.Length == 0;
    private bool NoModel => Models.Length == 0;
    private string SendLabel => Pending
        ? Resources.SkinAiRetrySubmission
        : Active ? Resources.AiQueueMessage : Resources.AiSend;

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Star(), GridLength.Auto],
        [
            Overlay([
                _transcript.Visible(!Empty)
                    .Automation(new() { AutomationId = "skin.ai.transcript" }),
                new SkinEditorEmptyState(LauncherAppearance.Capture(),"spark", NoModel ? Resources.SkinAiNoModel : Resources.SkinAiEmptyTitle, NoModel ? Resources.SkinAiNoModelHint : Resources.SkinAiEmptyHint).Visible(Empty)
                    .Automation(new()
                    {
                        Name = NoModel ? Resources.SkinAiNoModel : Resources.SkinAiEmptyTitle,
                        AutomationId = "skin.ai.empty"
                    }),
            ])
                .Cell(0, 0),
            Column([
                When(Conversation?.Responding == true || Workspace?.Preparing == true, new AiResponseIndicator()),
                new AiReferenceImages(DraftImages, id => ReadImage(id), id => RemoveImage(id), !Busy && !Pending).Visible(DraftImages.Length > 0),
                _prompt,
                Text(Resources.SkinAiPendingSubmission)
                    .FontSize(11f).Foreground(LauncherTheme.Warning)
                    .Wrap()
                    .Visible(Pending),
                Grid(
                    [GridLength.Star(), .. Enumerable.Repeat(GridLength.Auto, ComposerActions)],
                    [GridLength.Auto],
                    [
                        LauncherControls.Choice([..Models.Select(model => model.Name)]).MinHeight(40f)
                            .Placeholder(Resources.AiChooseModel)
                            .SelectedIndex(ModelIndex)
                            .OnSelectionChanged(index => SelectModel(index))
                            .Enabled(!Busy && !Active && !Pending && Models.Length > 0)
                            .Automation(new()
                            {
                                Name = Resources.AiChooseModel,
                                AutomationId = "skin.ai.model"
                            })
                            .Cell(0, 0),
                        LauncherControls.OutlineCommand(
                            Row([
                                LauncherIcons.Line("chat-add", 16f)
                            ]),
                            ShowNewConversation)
                            .Style(LauncherButtons.OutlineButton.Padding(0f)
                                .ContentAlignment(HorizontalAlignment.Center, VerticalAlignment.Center))
                            .Width(32f).Height(36f)
                            .Visible(HasConversation)
                            .Enabled(!Busy && !Pending)
                            .ToolTip(Resources.AiNewChat)
                            .Automation(new()
                            {
                                Name = Resources.AiNewChat,
                                AutomationId = "skin.ai.new"
                            })
                            .Cell(1, 0),
                        LauncherControls.OutlineCommand(
                            Row([
                                LauncherIcons.Line("image", 16f)
                            ]),
                            () => _ = AddImagesAsync(false))
                            .Style(LauncherButtons.OutlineButton.Padding(0f)
                                .ContentAlignment(HorizontalAlignment.Center, VerticalAlignment.Center))
                            .Width(32f).Height(36f)
                            .Enabled(CanAddImages)
                            .ToolTip(Resources.SkinAiAddImages)
                            .Automation(new()
                            {
                                Name = Resources.SkinAiAddImages,
                                AutomationId = "skin.ai.reference.add"
                            })
                            .Cell(HasConversation ? 2 : 1, 0),
                        LauncherControls.OutlineCommand(
                            Row([
                                LauncherIcons.Line("stop", 16f)
                            ]),
                            () => Stop())
                            .Style(LauncherButtons.OutlineButton.Padding(0f)
                                .ContentAlignment(HorizontalAlignment.Center, VerticalAlignment.Center))
                            .Width(36f).Height(36f)
                            .Visible(Active)
                            .ToolTip(Resources.AiStop)
                            .Automation(new()
                            {
                                Name = Resources.AiStop,
                                AutomationId = "skin.ai.stop"
                            })
                            .Cell(HasConversation ? 3 : 2, 0),
                        LauncherControls.OutlineCommand(
                            Row([
                                LauncherIcons.Line("forward", 16f)
                            ]),
                            () => Send())
                            .Style(LauncherButtons.OutlineButton.Padding(0f)
                                .ContentAlignment(HorizontalAlignment.Center, VerticalAlignment.Center))
                            .Width(36f).Height(36f)
                            .Enabled(CanSend)
                            .ToolTip(SendLabel)
                            .Automation(new()
                            {
                                Name = SendLabel,
                                AutomationId = "skin.ai.generate"
                            })
                            .Cell(ComposerActions, 0),
                    ])
                    .ColumnSpacing(8f),
            ])
                .Spacing(8f)
                .Padding(_editor.SectionPadding)
                .Cell(0, 1),
        ]);

    internal bool CanRetry(AiUiTurn turn) => !Busy && !_editor.Ai.IsEditing && _editor.Controller.LayerActions.CanChange && ShowRetry(turn);

    internal void Retry(AiUiTurn turn)
    {
        if (!CanRetry(turn))
            return;
        _scroll.ScrollToEnd();
        _ = _editor.Ai.SendAsync(turn);
    }

    internal bool EffectCheckFailed(AiUiTurn turn) => _editor.Ai.Current?.Ui.Chats.EffectCheckFailed(turn.Id) == true;

    internal void RunQueued(AiUiTurn turn)
    {
        if (Workspace is { } workspace && !Busy)
        {
            _scroll.ScrollToEnd();
            _ = workspace.Ui.RunAsync(async () => await workspace.Ui.Service.RunQueuedTurnAsync(workspace.Ui.Current.Id, turn.Id));
        }
    }

    internal void CancelQueued(AiUiTurn turn)
    {
        if (Workspace is { } workspace && !Busy)
            _ = workspace.Ui.RunAsync(async () => await workspace.Ui.Service.CancelTurnAsync(workspace.Ui.Current.Id, turn.Id));
    }

    private async Task AddImagesAsync(bool paste)
    {
        if (Conversation is not { } conversation || Workspace is not { } workspace || Busy || Pending)
            return;
        AddingImages = true;
        ReferenceError = string.Empty;
        try
        {
            var remaining = AiImageStore.MaximumImages - conversation.Images.Count;
            var images = paste ? await AiImagePicker.TryPasteAsync(workspace.Ui.Service, remaining) : await AiImagePicker.PickAsync(_editor.Controller.Window, workspace.Ui.Service, remaining);
            if (!_disposed && images is not null)
            {
                conversation.Images.AddRange(images);
                Revision++;
            }
        }
        catch (Exception error)
        {
            if (!_disposed)
                ReferenceError = error.Message;
        }
        finally
        {
            if (!_disposed)
                AddingImages = false;
        }
    }

    private void RemoveImage(string id)
    {
        if (Busy || Pending || Conversation is not { } conversation)
            return;
        conversation.Images.RemoveAll(image => image.Id == id);
        ReferenceError = string.Empty;
        Revision++;
    }

    private void KeyDown(KeyEvent key)
    {
        if (_editor.Panel != 1 || key.Key != Key.V || key.Modifiers != KeyModifiers.Control || key.Repeat || key.IsImeOwned || Busy || Pending)
            return;
        var focus = _editor.Controller.Window.FocusedElement;
        if (FindAnchor("skin.ai.prompt") is not { } prompt)
            return;
        while (focus is not null && !ReferenceEquals(focus, prompt))
            focus = focus.Parent;
        if (focus is null)
            return;
        try
        {
            if (AiImagePicker.HasClipboardImages())
                _ = AddImagesAsync(true);
        }
        catch (Exception error)
        {
            ReferenceError = error.Message;
        }
    }

    internal void ChangeTranscriptLayout(string id, Action change) => _scroll.ChangeLayout(id, change);
    internal bool PromptEnabled => !Pending && Models.Length > 0;

    internal void ChangeDraft(string value)
    {
        if (Conversation is { } conversation)
        {
            conversation.Draft = value;
            Revision++;
        }
    }

    internal void SubmitDraft()
    {
        if (CanSend)
            Send();
    }

    private void SelectModel(int index)
    {
        if (Conversation is not { } conversation || Active || Pending || index < 0 || index >= Models.Length)
            return;
        if (conversation.ModelId != Models[index].Id && Workspace is { } workspace)
            _ = workspace.Ui.Chats.ChangeContextAsync(conversation, modelId: Models[index].Id);
    }

    private void Send()
    {
        _scroll.ScrollToEnd();
        _ = _editor.Ai.SendAsync();
    }

    private void Stop()
    {
        if (Active && Workspace is { } workspace)
            _ = workspace.Ui.Chats.StopAsync(workspace.Ui.Current);
    }

    private void ShowNewConversation()
    {
        if (!HasConversation || Busy || Pending || Workspace is not { } workspace || ModalPopup.IsOpen(_editor.Controller.Window))
            return;
        SkinAiNewConversationDialog? dialog = null;
        var popup = new Flyout(close => dialog = new(workspace, close))
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false
        }.LauncherStyle(appearance => appearance.Menus.ErrorPopup.Padding(0f).MaxWidth(440f));
        popup.Closing += args => args.Cancel = dialog?.Working == true;
        ModalPopup.TryShow(_editor.Controller.Window, popup);
    }

    private void Refresh()
    {
        _scroll.Changed(_editor.Ai.Current?.Ui.Current);
        Revision++;
    }

    private InteractionNode? FindAnchor(string id) => Semantics is { } semantics && Interaction is { } interaction ? SettingsSearchLocator.Find(semantics, interaction, id) : null;

    private void SyncVisibility() => _scroll.Activate(_editor.Panel == 1, _editor.Ai.Current?.Ui.Current);

    protected override IDisposable? OnMounted()
    {
        _editor.Ai.Changed += Refresh;
        _editor.Controller.Window.LayoutUpdated += SyncVisibility;
        _editor.Controller.Window.KeyDown += KeyDown;
        _scroll.Attach();
        return null;
    }

    protected override void OnDispose()
    {
        _editor.Ai.Changed -= Refresh;
        _disposed = true;
        _editor.Controller.Window.KeyDown -= KeyDown;
        _editor.Controller.Window.LayoutUpdated -= SyncVisibility;
        _scroll.Dispose();
        base.OnDispose();
    }
}