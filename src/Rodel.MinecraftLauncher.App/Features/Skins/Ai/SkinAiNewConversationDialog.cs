// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Features.Ai;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Controls;
using Sprout.Layout;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Requires an explicit decision before replacing the project's only chat, including any active response.</summary>
internal sealed partial class SkinAiNewConversationDialog : Control
{
    private readonly SkinAiWorkspace _workspace;
    private readonly Action _close;
    private readonly AiUiConversation _previous;
    private AiConversationRemovalReview? _review;
    private bool _disposed;
    private partial bool Loading { get; set; }
    internal partial bool Working { get; private set; }
    internal partial string Error { get; private set; }
    private bool CanSubmit => !Loading && !Working && (!_previous.Persisted || _review is not null);

    internal SkinAiNewConversationDialog(SkinAiWorkspace workspace, Action close)
    {
        _workspace = workspace;
        _close = close;
        _previous = workspace.Ui.Current;
        Error = string.Empty;
    }

    private Ui Body => Border(Column([
        Border(Text(Resources.AiNewChat)
            .FontSize(18f).FontWeight(700).Foreground(LauncherTheme.Ink).Wrap()
            .Automation(new() { Name = Resources.AiNewChat, AutomationId = "ai.dialog.title" }))
            .Padding(20f, 16f),
        Border(Column([
            Text(Resources.SkinAiNewChatConfirm)
                .FontSize(13f).Foreground(LauncherTheme.Ink).Wrap(),
            InfoBar(message: Error)
                .Severity(InfoBarSeverity.Error)
                .Closable(false)
                .Open(Error.Length > 0)
                .Automation(new() { Name = Error, AutomationId = "skin.ai.new.error" }),
        ]).Spacing(12f))
            .Padding(20f, 0f, 20f, 20f),
        Border(Grid(
            [GridLength.Star(), GridLength.Star()],
            [GridLength.Auto],
            [
                LauncherControls.OutlineCommand(Resources.AiNewChat, () => Submit())
                    .Style(LauncherButtons.DialogSecondary)
                    .Enabled(CanSubmit)
                    .Automation(new() { Name = Resources.AiNewChat, AutomationId = "ai.dialog.submit" })
                    .Cell(0, 0),
                LauncherControls.Command(Resources.AiCancel, () => Close())
                    .Style(LauncherButtons.DialogPrimary)
                    .Enabled(!Working)
                    .Automation(new() { Name = Resources.AiCancel, AutomationId = "ai.dialog.secondary" })
                    .Cell(1, 0),
            ])
            .ColumnSpacing(12f))
            .Padding(20f, 14f, 20f, 18f)
            .BorderBrush(LauncherTheme.Edge)
            .BorderThickness(0f, 1f, 0f, 0f),
    ]))
        .BorderBrush(LauncherTheme.Edge)
        .BorderThickness(1f);

    private void Close()
    {
        if (!Working)
            _close();
    }

    private void Submit()
    {
        if (CanSubmit)
            _ = RestartAsync();
    }

    private async Task RestartAsync()
    {
        Working = true;
        var succeeded = await _workspace.RestartAsync(_previous, _review);
        if (_disposed)
            return;
        Working = false;
        if (succeeded)
            _close();
        else
            Error = _workspace.Ui.Error;
    }

    private async Task LoadReviewAsync()
    {
        if (!_previous.Persisted)
            return;
        Loading = true;
        try
        {
            var review = await Task.Run(() => _workspace.Ui.Service.ReviewConversationRemovalAsync(_previous.Id));
            if (!_disposed)
                _review = review;
        }
        catch (Exception error)
        {
            if (!_disposed)
                Error = error.Message;
        }
        finally
        {
            if (!_disposed)
                Loading = false;
        }
    }

    protected override IDisposable? OnMounted()
    {
        _ = LoadReviewAsync();
        return null;
    }

    protected override void OnDispose()
    {
        _disposed = true;
        base.OnDispose();
    }
}