// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Controls;

/// <summary>Offers the same file-selection callback through Windows copy-drag without committing an import.</summary>
/// <param name="accept">Rechecks the current page state and selected paths before accepting a copy.</param>
/// <param name="receive">Receives accepted, distinct paths through the feature's existing selection flow.</param>
/// <param name="hover">Updates target feedback and clears it when a drag leaves or finishes.</param>
internal sealed class FileDropHandler(
    Func<IReadOnlyList<string>, bool> accept,
    Action<IReadOnlyList<string>> receive,
    Action<bool> hover) : IDropHandler
{
    internal static FileDropHandler None { get; } = new(_ => false, _ => { }, _ => { });

    private bool Accept(DragDropEventArgs e) => e.Data.HasFiles && (e.AllowedEffects & DragEffect.Copy) != 0 && accept(e.Data.Files);

    /// <inheritdoc/>
    public void OnDragEnter(DragDropEventArgs e) => OnDragOver(e);

    /// <inheritdoc/>
    public void OnDragOver(DragDropEventArgs e)
    {
        var accepted = Accept(e);
        hover(accepted);
        e.AcceptedEffect = accepted ? DragEffect.Copy : DragEffect.None;
    }

    /// <inheritdoc/>
    public void OnDragLeave() => hover(false);

    /// <inheritdoc/>
    public void OnDrop(DragDropEventArgs e)
    {
        var accepted = Accept(e);
        hover(false);
        e.AcceptedEffect = accepted ? DragEffect.Copy : DragEffect.None;
        if (accepted) receive([.. e.Data.Files.Distinct(StringComparer.OrdinalIgnoreCase)]);
    }
}

/// <summary>Presents release feedback only while the target accepts dragged files.</summary>
/// <param name="Active">Whether this target currently accepts the dragged files.</param>
/// <param name="ReleaseText">Optional feature-specific release text.</param>
/// <param name="Id">Stable automation identity for the visible instruction.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct FileDropHint(
    LauncherAppearance Theme,
    bool Active,
    string? ReleaseText = null,
    string Id = "files.drop.hint")
{
    internal string Hint => ReleaseText ?? Resources.FileDropRelease;

    /// <summary>Shows release feedback while the target accepts a copy; otherwise occupies no space.</summary>
    public Ui Body => Text(Hint)
        .FontSize(12f).Foreground(Theme.Focus)
        .Wrap()
        .Automation(new() { Name = Hint, AutomationId = Id })
        .Visible(Active);
}