// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Instances;
using Sprout;
using Sprout.Graphics;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Import;

[View]
internal readonly partial record struct LinkedInstanceRow(
    LauncherAppearance Theme,
    InstanceImportPreview Preview,
    bool Selected,
    string? GameDirectory,
    bool Enabled,
    Action Toggle,
    Action SelectShared,
    Action SelectVersion)
{
    /// <summary>Shows one existing version's actual paths and any required isolation choice with shared list material.</summary>
    public Ui Body => Border(Column([
        new LauncherCheckBox(Theme, Preview.Name is { Length: > 0 } name ? name + " · " + Preview.VersionId : Preview.VersionId!, Selected,
            Toggle, Enabled, "link.version." + Preview.VersionId + "/" + Preview.ProfileId),
        Text(GameDirectory ?? Preview.GameDirectory).FontSize(11f).Foreground(Theme.Muted).Wrap(),
        Text(Resources.ImportChooseGameDirectory).FontSize(12f).Foreground(Theme.Muted).Wrap()
            .Visible(Selected && Preview.NeedsGameDirectory && GameDirectory is null),
        Column([
            new ImportChoice(Theme, Preview.MinecraftDirectory, "link.game." + Preview.VersionId + ".shared",
                GameDirectory == Preview.MinecraftDirectory, Enabled, SelectShared),
            new ImportChoice(Theme, Preview.GameDirectoryCandidates.LastOrDefault() ?? string.Empty, "link.game." + Preview.VersionId + ".version",
                GameDirectory is not null && GameDirectory == Preview.GameDirectoryCandidates.LastOrDefault(), Enabled, SelectVersion),
        ]).Spacing(4f)
            .Visible(Selected && Preview.NeedsGameDirectory),
    ]).Spacing(8f).Padding(12f))
        .BorderBrush(Theme.Edge).BorderThickness(0f, 0f, 0f, 1f);
}
