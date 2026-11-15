// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Installation;
using Sprout.Controls;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Controls.Versions;

/// <summary>Builds the same editable, grouped version picker for ordinary creation and room preparation.</summary>
internal static class GameVersionPicker
{
    /// <summary>Uses the caller's Core catalog snapshot; only real versions can reach the selection callback.</summary>
    internal static ComboBox Create(VersionCatalog? catalog, string value, Action<string> changed, Action validate, string automationId, bool enabled, bool invalid = false)
    {
        var choices = new GameVersionChoices(catalog);
        var labels = choices.Rows.Select(row => row.Header ? Header(row.Group) : row.Value).ToArray();
        var headers = choices.Rows.Where(row => row.Header).ToDictionary(row => Header(row.Group), row => row.Group);
        var source = new ComboBoxItemSource(
            choices.Rows.Count,
            index => labels[index],
            index => choices.Rows[index].Header
                ? Text(labels[index]).FontSize(11f).Foreground(LauncherTheme.Muted)
                : Text(labels[index]).Style(LauncherStyles.Text) with { InheritsForeground = true },
            enabledAt: index => choices.VersionAt(index) is not null,
            automationAt: index => new()
            {
                Name = labels[index],
                AutomationId = automationId + (choices.Rows[index].Header ? ".group." : ".item.") + choices.Rows[index].Value,
            });
        return LauncherControls.Choice(source)
            .IsEditable(true)
            .Text(value)
            .OnTextChanged(changed)
            .OnSelectionChanged(index =>
            {
                if (choices.VersionAt(index) is not { } version)
                    return;
                changed(version);
                validate();
            })
            .OnTextSubmitted(_ => validate())
            .OnDropDownClosed(validate)
            .OnEditorFocusChanged(focused => { if (!focused) validate(); })
            .Filter((query, text) => headers.TryGetValue(text, out var group) ? choices.GroupMatches(group, query) : text.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Placeholder(Resources.CreateVersionPlaceholder)
            .Enabled(enabled)
            .SurfacePainter(invalid ? LauncherMenus.CriticalChoiceSurface : LauncherMenus.ChoiceSurface);
    }

    private static string Header(string group) => group switch
    {
        "release" => Resources.CreateReleases,
        "snapshot" => Resources.CreateSnapshots,
        "historical" => Resources.CreateHistorical,
        _ => Resources.GameVersionOther,
    };
}