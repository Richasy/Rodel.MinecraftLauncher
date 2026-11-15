// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Shows the complete frozen review on demand; closing details never approves or rejects the request.</summary>
internal sealed partial class AiReviewDialog : Control
{
    private readonly AiUiState _state;
    private readonly Window _window;
    private readonly AiReviewFields _fields;

    internal AiReviewDialog(
        AiUiState state,
        AiApproval approval,
        Window window,
        Action close)
    {
        _state = state;
        Approval = approval;
        _window = window;
        CloseAction = close;
        _fields = new(this);
    }

    internal partial int Revision { get; private set; }
    internal AiApproval Approval
    {
        get
        {
            _ = Revision;
            return field;
        }
    }

    private Action CloseAction { get; }

    private Ui Body => new AiDialogFrame(
LauncherAppearance.Capture(), AiDialogAppearance.Capture(),
        Resources.AiDetails,
        _fields,
        CloseAction,
        string.Empty,
        CloseAction,
        false,
        Math.Min(620f, _window.ClientSize.Height - 64f),
        SingleAction: true);

    private void Changed() => Revision++;

    protected override IDisposable? OnMounted()
    {
        _state.Changed += Changed;
        _ = Approval.ReadAsync();
        return null;
    }

    protected override void OnDispose()
    {
        _state.Changed -= Changed;
        base.OnDispose();
    }
}

internal sealed partial class AiReviewFields(AiReviewDialog owner) : Control
{
    private Ui Body => Column([
        Text(owner.Approval.OperationPermission ? Resources.AiOperationPermissionNotice : Resources.AiReviewNotice)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Wrap(),
        Text(Resources.AiApprovalInactive)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(!owner.Approval.IsActive),
        new AiJsonPreview(LauncherAppearance.Capture(),owner.Approval.Text, "ai.review.text"),
        Text(owner.Approval.Error)
            .Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(owner.Approval.Error.Length > 0),
        LauncherControls.OutlineCommand(Resources.AiReadMore, () => _ = owner.Approval.ReadAsync())
            .Style(LauncherButtons.AiTextAction)
            .Visible(!owner.Approval.DetailsComplete)
            .Enabled(!owner.Approval.Working)
            .Automation(new()
            {
                Name = Resources.AiReadMore,
                AutomationId = "ai.review.more"
            }),
    ])
        .Spacing(14f);
}