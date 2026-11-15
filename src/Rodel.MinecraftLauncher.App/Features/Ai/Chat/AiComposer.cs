// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
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
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Keeps the draft and context selectors in one focus surface without executing a request.</summary>
internal sealed partial class AiComposer(AiPage owner) : Control
{
    private partial bool Focused { get; set; }
    private bool _disposed, _focusPending;
    private FileDropHandler Drop => field ??= new(AcceptDrop, ReceiveDrop, value => Dropping = value);
    private partial bool Dropping { get; set; }
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private bool Repair => owner.Conversation.RepairId is not null;
    private static ButtonStyle ContextStyle => LauncherButtons.AiTextAction.Padding(new Thickness(10f, 10f, 0f, 10f)).LabelStyle(LauncherStyles.Text.FontSize(12f));
    private static TextBoxPalette EditorPalette => LauncherStyles.InputPalette with
    {
        Background = Colors.Transparent,
        BackgroundFocused = Colors.Transparent,
        BackgroundPointerOver = Colors.Transparent,
        RestingElevation = ElevationProfile.TextField(Colors.Transparent, Colors.Transparent),
        FocusedElevation = ElevationProfile.TextField(Colors.Transparent, Colors.Transparent),
    };
    private static ComboBoxPalette ContextPalette
    {
        get
        {
            var theme = LauncherAppearance.Capture();
            return theme.Menus.Choice with
            {
                ContentOffsets = default,
                Faceplate = theme.ButtonPalette.Quiet,
            };
        }
    }

    private Ui Body => DropTarget(Column([Border(Column([
            Grid(
                [GridLength.Star(), GridLength.Auto],
                [GridLength.Auto],
                [
                    Row([
                    ComboBox(owner.InstanceNames)
                        .FaceplateSizing(ComboBoxFaceplateSizing.SelectedItem)
                        .SelectedIndex(owner.InstanceIndex)
                        .OnSelectionChanged(owner.SelectInstance)
                        .Visible(!Repair)
                        .Enabled(!owner.Busy && owner.Conversation.Submission is null)
                        .Palette(ContextPalette)
                        .FaceplateStyle(ContextStyle)
                        .DropDownStyle(LauncherMenus.ChoiceMenu)
                        .DropDownPlacement(PopupPlacement.BottomEdgeAlignedLeft)
                        .ItemHeight(36f)
                        .Automation(new()
                        {
                            Name = Resources.AiInstance,
                            AutomationId = "ai.context.instance"
                        })
                        .Shrink(1f)
                        .MinContentWidth(0f),
                    LauncherControls.OutlineCommand(
                        Grid(
                            [GridLength.Auto, GridLength.Star()],
                            [GridLength.Auto],
                            [
                                LauncherIcons.Line("instances", 14f)
                                    .VAlign(VerticalAlignment.Center)
                                    .Cell(0, 0),
                                Text(owner.ContextName(owner.Conversation))
                                    .FontSize(12f).Foreground(LauncherTheme.Ink)
                                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                                    .VAlign(VerticalAlignment.Center)
                                    .Cell(1, 0),
                            ])
                            .ColumnSpacing(6f),
                        owner.ShowRepairInstance)
                        .Style(ContextStyle.ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Center))
                        .Visible(Repair)
                        .Automation(new()
                        {
                            Name = string.Format(Resources.AiRepairInstanceView, owner.ContextName(owner.Conversation)),
                            AutomationId = "ai.context.instance"
                        })
                        .Shrink(1f)
                        .MinContentWidth(0f),
                    ComboBox([Resources.AiSafeMode, Resources.AiAllowAllMode])
                        .FaceplateSizing(ComboBoxFaceplateSizing.SelectedItem)
                        .SelectedIndex(owner.Conversation.Operate ? 1 : 0)
                        .OnSelectionChanged(owner.SelectMode)
                        .Visible(!Repair)
                        .Enabled(!owner.Busy && owner.Conversation.Submission is null)
                        .Palette(ContextPalette)
                        .FaceplateStyle(ContextStyle)
                        .DropDownStyle(LauncherMenus.ChoiceMenu)
                        .DropDownPlacement(PopupPlacement.BottomEdgeAlignedLeft)
                        .ItemHeight(36f)
                        .Automation(new()
                        {
                            Name = Resources.AiMode,
                            AutomationId = "ai.context.mode"
                        }),
                    ComboBox(owner.ModelNames)
                        .FaceplateSizing(ComboBoxFaceplateSizing.SelectedItem)
                        .Placeholder(Resources.AiChooseModel)
                        .SelectedIndex(owner.ModelIndex)
                        .OnSelectionChanged(owner.SelectModel)
                        .Enabled(!owner.Busy && owner.Conversation.Submission is null && owner.Ready)
                        .Palette(ContextPalette)
                        .FaceplateStyle(ContextStyle)
                        .DropDownStyle(LauncherMenus.ChoiceMenu)
                        .DropDownPlacement(PopupPlacement.BottomEdgeAlignedLeft)
                        .ItemHeight(36f)
                        .Automation(new()
                        {
                            Name = Resources.AiChooseModel,
                            AutomationId = "ai.context.model"
                        })
                        .Shrink(1f)
                        .MinContentWidth(0f),
                    ])
                        .Spacing(4f)
                        .Cell(0, 0),
                    LauncherControls.OutlineCommand(
                        Row([
                            LauncherIcons.Line("image", 16f)
                        ]),
                        () => _ = owner.AddImagesAsync(false))
                        .Style(LauncherButtons.PopupCloseButton)
                        .Width(32f).Height(36f)
                        .VAlign(VerticalAlignment.Center)
                        .HAlign(HorizontalAlignment.Right)
                        .Enabled(owner.CanAddImages)
                        .ToolTip(Resources.SkinAiAddImages)
                        .Automation(new()
                        {
                            Name = Resources.SkinAiAddImages,
                            AutomationId = "ai.reference.add"
                        })
                        .Cell(1, 0),
                ])
                .ColumnSpacing(4f),
            new AiReferenceImages([..owner.Conversation.Images], id => owner.State.Service.Images.ReadBytes(id), owner.RemoveImage, !owner.AddingImages && !owner.Busy && owner.Conversation.Submission is null).Visible(owner.Conversation.Images.Count > 0)
                .Margin(12f, 0f),
            Text(owner.ImageError ?? string.Empty)
                .FontSize(11f).Foreground(LauncherTheme.Error)
                .Wrap()
                .Visible(!string.IsNullOrEmpty(owner.ImageError))
                .Margin(12f, 0f),
            TextBox(owner.Conversation.Draft, owner.ChangeDraft)
                .AcceptsReturn()
                .SubmitOnEnter()
                .OnSubmitted(_ =>
                {
                    if (owner.CanSend)
                        owner.Send();
                })
                .Wrap()
                .ClearButton(false)
                .OnGotFocus(_ => UpdateFocus())
                .OnLostFocus(UpdateFocus)
                .Placeholder(Resources.AiQuestion)
                .Style(LauncherStyles.Input.ContentPadding(Thickness.Zero))
                .Palette(EditorPalette)
                .MinHeight(owner.Conversation.RepairId is not null
                    ? 36f
                    : owner.Viewport.Height < 700f ? 44f : 64f)
                .MaxHeight(Math.Max(96f, owner.Viewport.Height * .2f))
                .Automation(new()
                {
                    Name = Resources.AiInputName,
                    AutomationId = "ai.input"
                })
                .Margin(new Thickness(12f, 0f, 12f, 0f)),
            Grid(
                [GridLength.Star(), GridLength.Auto],
                [GridLength.Auto],
                [
                    Text(Resources.AiInputHelp)
                        .Automation(new()
                        {
                            Name = Resources.AiInputHelp,
                            AutomationId = "ai.input.help"
                        })
                        .FontSize(10f).Foreground(LauncherTheme.Quiet)
                        .Wrap()
                        .VAlign(VerticalAlignment.Bottom)
                        .Cell(0, 0),
                    Row([
                        LauncherControls.OutlineCommand(
                            Row([
                                LauncherIcons.Line("stop", 16f)
                            ]),
                            owner.Stop)
                            .Style(LauncherButtons.PopupCloseButton)
                            .Width(32f).Height(32f)
                            .Visible(owner.Conversation.Active)
                            .Enabled(!owner.Busy || owner.Conversation.Sending)
                            .ToolTip(Resources.AiStop)
                            .Automation(new()
                            {
                                Name = Resources.AiStop,
                                AutomationId = "ai.stop"
                            }),
                        LauncherControls.OutlineCommand(
                            Row([
                                LauncherIcons.Line("forward", 16f),
                                Text(owner.Conversation.Active ? Resources.AiQueueMessage : Resources.AiSend)
                            ])
                                .Spacing(8f),
                            owner.Send)
                            .Style(LauncherButtons.OutlineButton.Padding(12f, 7f))
                            .Enabled(owner.CanSend)
                            .Automation(new()
                            {
                                Name = owner.Conversation.Active ? Resources.AiQueueMessage : Resources.AiSend,
                                AutomationId = "ai.send"
                            }),
                    ])
                        .Spacing(6f)
                        .Cell(1, 0),
                ])
                .ColumnSpacing(12f)
                .Margin(new Thickness(12f, 0f, 12f, 12f)),
        ])
            .Spacing(8f))
        .Background(LauncherTheme.ControlBackground)
        .BorderBrush(Focused ? LauncherTheme.Focus : LauncherTheme.Edge)
        .BorderThickness(1f), new FileDropHint(LauncherAppearance.Capture(),Dropping)]).Spacing(6f), Drop);

    private bool AcceptDrop(IReadOnlyList<string> paths) => owner.CanAddImages
        && AiImagePicker.CanDrop(paths, AiImageStore.MaximumImages - owner.Conversation.Images.Count);

    private void ReceiveDrop(IReadOnlyList<string> paths) => _ = owner.AddImagesAsync(false, paths);

    private void UpdateFocus()
    {
        var node = owner.Window.FocusedElement;
        var inside = false;
        while (node is not null)
        {
            if (ReferenceEquals(node, Interaction))
            {
                inside = true;
                break;
            }

            node = node.Parent;
        }

        Focused = inside && owner.Window.IsKeyboardFocusVisible;
    }

    private void ScheduleFocus()
    {
        if (_disposed || _focusPending)
            return;
        _focusPending = true;
        if (!_dispatcher.TryEnqueue(() =>
            {
                _focusPending = false;
                if (!_disposed)
                    UpdateFocus();
            }))
        {
            _focusPending = false;
        }
    }

    private void OnPointer(PointerEvent _) => ScheduleFocus();

    private void OnKey(KeyEvent _) => ScheduleFocus();

    protected override IDisposable? OnMounted()
    {
        owner.Window.PointerPressed += OnPointer;
        owner.Window.KeyDown += OnKey;
        ScheduleFocus();
        return null;
    }

    protected override void OnDispose()
    {
        _disposed = true;
        owner.Window.PointerPressed -= OnPointer;
        owner.Window.KeyDown -= OnKey;
        base.OnDispose();
    }
}