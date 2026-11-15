// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Features.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Retains the native workspace while Core owns durable turns and settings.</summary>
internal sealed partial class AiPage : Control
{
    internal readonly AiUiState State;
    internal readonly Window Window;
    internal readonly HomeController Home;
    private readonly Action<string> _openSettings;
    internal readonly AiComposer Composer;
    internal readonly AiRepairBanner RepairBanner;
    internal readonly AiApprovalBanner ApprovalBanner;
    internal readonly ScrollView Transcript;
    internal readonly AiHistoryPanel History;
    private AiHistoryPanel? _drawerHistory;
    private Flyout? _history;
    private Flyout? _dialog;
    private MenuFlyout? _menu;
    private readonly AiTranscriptScroll _scroll;
    private bool _active;

    internal AiPage(
        AiUiState state,
        HomeController home,
        Window window,
        Action<string> openSettings)
    {
        State = state;
        Home = home;
        Window = window;
        _openSettings = openSettings;
        Viewport = window.ClientSize;
        Composer = new(this);
        ApprovalBanner = new(this);
        RepairBanner = new(this);
        Transcript = new(new AiTranscript(this));
        History = new(NewConversation, SelectConversation, ConversationMenu);
        _scroll = new(Transcript, window, FindAnchor);
        State.SetTranscriptActive(false);
        State.IsReading = () => _active && Window.IsActive;
    }

    internal partial int Revision { get; private set; }
    internal partial Size Viewport { get; private set; }
    internal bool InlineHistory => Viewport.Width >= 1100f;
    internal AiUiConversation Conversation
    {
        get
        {
            _ = Revision;
            return State.Current;
        }
    }

    internal AiApproval? Approval
    {
        get
        {
            _ = Revision;
            return Conversation.Turns.Select(turn => turn.Approval).FirstOrDefault(approval => approval is { IsActive: true });
        }
    }

    internal bool Ready => State.Configuration.HasConfiguredModel;
    internal bool Busy
    {
        get
        {
            _ = Revision;
            return State.Busy;
        }
    }

    internal bool CanSend
    {
        get
        {
            _ = Revision;
            return State.Loaded && !State.Busy && !Conversation.HistoryLoading && !Conversation.HistoryFailed && !AddingImages && (Conversation.PendingTurn is not null || Conversation.Submission is not null || ((!string.IsNullOrWhiteSpace(Conversation.Draft) || Conversation.Images.Count > 0) && State.Configuration.Providers.Any(provider => provider.Enabled && provider.Models.Any(model => model.Id == Conversation.ModelId))));
        }
    }

    internal partial bool AddingImages { get; private set; }
    internal partial string ImageError { get; private set; }
    private bool _disposed;
    internal bool CanAddImages => !AddingImages && !Busy && Conversation.Submission is null && Conversation.Images.Count < AiImageStore.MaximumImages;

    internal async Task AddImagesAsync(bool paste, IReadOnlyList<string>? paths = null)
    {
        if (AddingImages || Busy || Conversation.Submission is not null)
            return;
        var conversation = Conversation;
        AddingImages = true;
        ImageError = string.Empty;
        try
        {
            var remaining = AiImageStore.MaximumImages - conversation.Images.Count;
            var images = paths is not null ? await AiImagePicker.FromFilesAsync(State.Service, paths, remaining)
                : paste ? await AiImagePicker.TryPasteAsync(State.Service, remaining) : await AiImagePicker.PickAsync(Window, State.Service, remaining);
            if (!_disposed && images is { Length: > 0 } && conversation.Submission is null)
            {
                conversation.Images.AddRange(images);
                if (!State.Conversations.Contains(conversation))
                    State.Conversations.Insert(0, conversation);
                State.Notify();
            }
        }
        catch (Exception error)
        {
            if (!_disposed)
                ImageError = error.Message;
        }
        finally
        {
            if (!_disposed)
                AddingImages = false;
        }
    }

    internal void RemoveImage(string id)
    {
        if (AddingImages || Busy || Conversation.Submission is not null)
            return;
        Conversation.Images.RemoveAll(image => image.Id == id);
        ImageError = string.Empty;
        State.Notify();
    }

    private void PasteImage(KeyEvent key)
    {
        if (!_active || key.Key != Key.V || key.Modifiers != KeyModifiers.Control || key.Repeat || key.IsImeOwned || AddingImages || Busy || Conversation.Submission is not null)
            return;
        var focus = Window.FocusedElement;
        if (FindAnchor("ai.input") is not { } input)
            return;
        while (focus is not null && !ReferenceEquals(focus, input))
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
            ImageError = error.Message;
        }
    }

    internal void Send() => Send(null);

    internal void Send(AiUiTurn? retry)
    {
        _scroll.ScrollToEnd();
        _ = State.Chats.SendAsync(Conversation, retry);
    }

    internal void RunQueued(AiUiTurn turn)
    {
        _scroll.ScrollToEnd();
        var conversation = Conversation;
        _ = State.RunAsync(async () => await State.Service.RunQueuedTurnAsync(conversation.Id, turn.Id));
    }

    internal void Stop() => _ = State.Chats.StopAsync(Conversation);

    internal void Activate(bool active)
    {
        if (active && !State.Loaded)
            _ = State.LoadAsync();
        if (_active == active)
            return;
        if (!active)
        {
            _history?.Hide();
            _menu?.Hide();
            _dialog?.Hide();
        }

        _active = active;
        _scroll.Activate(active, State.Current);
        State.SetTranscriptActive(active);
        if (!active)
            Revision++;
        if (active)
        {
            State.MarkRead();
            RefreshHistory();
            Revision++;
            if (Conversation.Persisted)
                _ = RestoreConversationAsync(Conversation);
            _ = State.Chats.ContinueRepairAsync(Conversation);
        }
    }

    internal string ModelName
    {
        get
        {
            _ = Revision;
            return State.Configuration.Providers.SelectMany(provider => provider.Models).FirstOrDefault(model => model.Id == Conversation.ModelId)?.Name ?? Resources.AiChooseModel;
        }
    }

    internal string[] InstanceNames
    {
        get
        {
            _ = Revision;
            return [Resources.AiNoInstance, .. Home.State.Instances.Select(instance => instance.DisplayName)];
        }
    }

    internal int InstanceIndex
    {
        get
        {
            _ = Revision;
            return Array.FindIndex(Home.State.Instances.ToArray(), instance => instance.Id == Conversation.InstanceId) + 1;
        }
    }

    internal string ContextName(AiUiConversation conversation) => Home.State.Instances.FirstOrDefault(instance => instance.Id == conversation.InstanceId)?.DisplayName ?? (conversation.RepairId is not null ? conversation.InstanceId ?? Resources.UnreadableInstance : Resources.AiNoInstance);
    internal AiPage Self => this;

    private Ui Body => new AiWorkspaceView(
LauncherAppearance.Capture(), Self,
        InlineHistory,
        LauncherMaterials.Acrylic,
        LauncherTheme.Ink,
        LauncherTheme.Edge,
        LauncherButtons.PopupCloseButton,
        Conversation.Title,
        Conversation.RepairId is not null,
        Conversation.Responding,
        Approval is { IsActive: true, PathAccess: null },
        Approval?.Name ?? string.Empty);

    internal void OpenSettings() => _openSettings("settings.ai.services");

    internal void OpenAccess() => _openSettings("settings.ai.folders");

    internal void ShowRepairInstance()
    {
        if (Conversation.RepairId is null || ModalPopup.IsOpen(Window))
            return;
        var instance = Home.State.Instances.FirstOrDefault(item => item.Id == Conversation.InstanceId);
        var name = ContextName(Conversation);
        _dialog?.Hide();
        _dialog = new Flyout(() => new AiRepairInstanceDetails(instance, name))
        {
            Placement = PopupPlacement.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = true
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.MaxWidth(360f).Padding(0f)).Automation(new()
        {
            Name = Resources.AiRepairInstanceTitle,
            AutomationId = "ai.repair.instance.details"
        });
        if (FindAnchor("ai.context.instance") is { } anchor)
            _dialog.ShowAt(Window, anchor);
        else
            _dialog.ShowAt(Window);
    }

    internal void SelectInstance(int index)
    {
        _ = State.Chats.ChangeContextAsync(Conversation, index > 0 && index <= Home.State.Instances.Count ? Home.State.Instances[index - 1].Id : null, instance: true);
    }

    internal void SelectMode(int index)
    {
        if (index < 0)
            return;
        Conversation.ModeSelected = true;
        _ = State.Chats.ChangeAccessAsync(Conversation, operate: index == 1);
    }

    internal void ChangeDraft(string value)
    {
        Conversation.Draft = value;
        if (!State.Conversations.Contains(Conversation))
            State.Conversations.Insert(0, Conversation);
        State.Notify();
    }

    internal void Suggest(string value)
    {
        ChangeDraft(value);
    }

    internal void NewConversation()
    {
        State.NewConversation();
        _history?.Hide();
    }

    private AiUiConversation? FindHistoryConversation(string key)
    {
        var conversation = State.Conversations.FirstOrDefault(item => item.UiKey == key);
        if (conversation is null)
            State.Report(new InvalidOperationException(Resources.AiInvalidRecord));
        return conversation;
    }

    private void SelectConversation(string key)
    {
        _history?.Hide();
        if (FindHistoryConversation(key) is not { } conversation)
            return;
        if (ReferenceEquals(Conversation, conversation))
            return;
        State.Select(conversation);
        _ = RestoreConversationAsync(conversation);
    }

    private async Task RestoreConversationAsync(AiUiConversation conversation)
    {
        await State.Chats.SelectAsync(conversation);
        if (!ReferenceEquals(Conversation, conversation))
            return;
        _scroll.Restore();
        if (_active)
            Revision++;
    }

    private AiUiModel[] Models => [.. State.Configuration.Providers.Where(provider => provider.Enabled).SelectMany(provider => provider.Models)];
    internal string[] ModelNames => Models.Length > 0 ? [.. Models.Select(model => model.Name)] : [Resources.AiChooseModel];
    internal int ModelIndex => Array.FindIndex(Models, model => model.Id == Conversation.ModelId);

    internal void SelectModel(int index)
    {
        var models = Models;
        if (!Busy && Conversation.Submission is null && index >= 0 && index < models.Length)
        {
            _ = ChangeModelAsync(models[index].Id);
        }
    }

    private async Task ChangeModelAsync(string modelId)
    {
        await State.Chats.ChangeContextAsync(Conversation, modelId: modelId);
        await State.Chats.ContinueRepairAsync(Conversation);
    }

    internal void ShowHistory()
    {
        if (!_active || InlineHistory || _history is { IsOpen: true })
            return;
        var content = new AiHistoryPanel(
            NewConversation,
            SelectConversation,
            ConversationMenu,
            true);
        _drawerHistory = content;
        RefreshHistory();
        var flyout = new Flyout(() => content)
        {
            Placement = PopupPlacement.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = true,
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.MaxWidth(286f)).Automation(new() { AutomationId = "ai.history.flyout" });
        _history = flyout;
        flyout.Closed += () =>
        {
            if (ReferenceEquals(_history, flyout))
            {
                _history = null;
                _drawerHistory = null;
            }
        };
        if (FindAnchor("ai.history.open") is { } anchor)
            flyout.ShowAt(Window, anchor);
        else
            flyout.ShowAt(Window);
    }

    internal void HideHistory() => _history?.Hide();

    private MenuFlyout? ConversationMenu(string key)
    {
        if (FindHistoryConversation(key) is not { } conversation)
            return null;
        _menu = new MenuFlyout(
            new MenuFlyoutItem(Resources.AiRenameChat, () => ShowConversationDialog(AiConversationAction.Rename, conversation))
            {
                Icon = LauncherIcons.Element("edit", 17f),
                Automation = new() { AutomationId = "ai.history.rename" }
            },
            new MenuFlyoutItem(Resources.AiCopyDraft, () => Sprout.Host.Clipboard.SetText(conversation.Draft))
            {
                Icon = LauncherIcons.Element("copy", 17f),
                IsEnabled = conversation.Draft.Length > 0
            },
            new MenuFlyoutItem(Resources.AiExport, () => ShowConversationDialog(AiConversationAction.Export, conversation))
            {
                Icon = LauncherIcons.Element("download", 17f),
                Automation = new() { AutomationId = "ai.history.export" }
            },
            new MenuFlyoutSeparator(),
            new MenuFlyoutItem(Resources.AiDiscardDraft, () => ShowConversationDialog(AiConversationAction.Remove, conversation))
            {
                Icon = LauncherIcons.Element("delete", 17f, LauncherTheme.Error),
                Automation = new() { AutomationId = "ai.history.delete" }
            }).Style(LauncherMenus.CommandMenu);
        return _menu;
    }

    private void ShowConversationDialog(AiConversationAction action, AiUiConversation conversation)
    {
        if (ModalPopup.IsOpen(Window))
            return;
        _dialog = new Flyout(close => new AiConversationDialog(
                State,
                Window,
                action,
                conversation,
                close))
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false,
            ShouldConstrainToRootBounds = true,
            Transition = PopupTransition.None
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.MaxWidth(560f).Padding(0f));
        _dialog.Automation(new()
        {
            AutomationId = "ai.dialog",
            IsDialog = true
        });
        ModalPopup.TryShow(Window, _dialog);
    }

    internal void ShowReview(AiApproval approval)
    {
        if (ModalPopup.IsOpen(Window))
            return;
        _dialog = new Flyout(close => new AiReviewDialog(State, approval, Window, close))
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false,
            ShouldConstrainToRootBounds = true,
            Transition = PopupTransition.None
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.MaxWidth(680f).Padding(0f));
        _dialog.Automation(new()
        {
            AutomationId = "ai.dialog",
            IsDialog = true
        });
        ModalPopup.TryShow(Window, _dialog);
    }

    private InteractionNode? FindAnchor(string id) => Semantics is { } semantics && Interaction is { } interaction ? SettingsSearchLocator.Find(semantics, interaction, id) : null;

    private void Changed()
    {
        _scroll.Changed(State.Current);
        if (!_active)
            return;
        RefreshHistory();
        Revision++;
    }

    private void RefreshHistory()
    {
        var items = State.Conversations.Select(conversation => new AiHistoryItem(
                conversation.UiKey,
                conversation.Id,
                conversation.Name,
                conversation.InstanceId,
                Home.State.Instances.FirstOrDefault(instance => instance.Id == conversation.InstanceId)?.Name,
                conversation.RepairId is not null,
                ReferenceEquals(State.Current, conversation),
                AiHistoryItem.ReadBadge(conversation.DisplayTurns.Select(turn => turn.Record), conversation.ReadReceipt),
                conversation.NeedsInput)).ToArray();
        History.SetItems(items);
        _drawerHistory?.SetItems(items);
    }

    internal void ChangeTranscriptLayout(string id, Action change) => _scroll.ChangeLayout(id, change);

    private void OnHomeChanged(HomePageState state)
    {
        if (_active)
        {
            RefreshHistory();
            Revision++;
        }
    }

    private void SizeChanged(object? sender, WindowSizeChangedEventArgs args)
    {
        Viewport = args.Size;
        if (InlineHistory)
            HideHistory();
    }

    private void Activated(object? sender, EventArgs args)
    {
        if (_active)
        {
            State.MarkRead();
            RefreshHistory();
            Revision++;
        }
    }

    protected override IDisposable? OnMounted()
    {
        State.Changed += Changed;
        Home.StateChanged += OnHomeChanged;
        Window.SizeChanged += SizeChanged;
        _scroll.Attach();
        Window.KeyDown += PasteImage;
        Window.Activated += Activated;
        RefreshHistory();
        return null;
    }

    protected override void OnDispose()
    {
        State.Changed -= Changed;
        Home.StateChanged -= OnHomeChanged;
        Window.SizeChanged -= SizeChanged;
        _disposed = true;
        Window.KeyDown -= PasteImage;
        _scroll.Dispose();
        Window.Activated -= Activated;
        State.IsReading = null;
        _history?.Hide();
        _menu?.Hide();
        _dialog?.Hide();
        base.OnDispose();
    }
}