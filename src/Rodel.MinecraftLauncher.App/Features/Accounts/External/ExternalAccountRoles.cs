// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Accounts;

internal sealed partial class ExternalAccountRoles : Control
{
    private readonly ExternalAccountPopup _owner;
    private readonly PoolingItemFactory<ExternalRoleGroup, ExternalRoleChoices> _factory;
    private readonly ItemsRepeater _repeater;

    internal ExternalAccountRoles(ExternalAccountPopup owner)
    {
        _owner = owner;
        _factory = new([], _ => 0, group => new ExternalRoleChoices(LauncherAppearance.Capture(), group.Profiles, group.Selected, group.Select), autoProject: true);
        _repeater = new(_factory, new StackLayout { EstimatedItemExtent = 40f });
    }

    internal ExternalAccountRoles WithRoles(IReadOnlyList<ExternalAuthProfile>? profiles, string selected)
    {
        // Native RadioButtons requires a nonempty group even while collapsed. The repeater creates no group until real roles arrive.
        _factory.UpdateItems(profiles is { Count: > 0 } ? [new([..profiles], selected, index =>
                    {
                        if (index >= 0 && index < profiles.Count)
                            _owner.SelectedRole = profiles[index].Id;
                    })] : []);
        return this;
    }

    private Ui Body => _repeater;
}

/// <summary>One actual offered role group; an empty attempt produces no group.</summary>
/// <param name = "Profiles">Profiles offered by Core.</param>
/// <param name = "Selected">The selected role UUID, or an empty string.</param>
/// <param name = "Select">Updates the selection without committing authentication.</param>
internal sealed record ExternalRoleGroup(ExternalAuthProfile[] Profiles, string Selected, Action<int> Select);

/// <summary>Uses the native keyboard and selection model only for a nonempty offered group.</summary>
/// <param name = "Profiles">The nonempty offered profile list.</param>
/// <param name = "Selected">The selected UUID, if any.</param>
/// <param name = "Select">The native selection callback.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct ExternalRoleChoices(LauncherAppearance Theme, ExternalAuthProfile[] Profiles, string Selected, Action<int> Select)
{
    internal int SelectedIndex
    {
        get
        {
            var selected = Selected;
            return Array.FindIndex(Profiles, profile => profile.Id == selected);
        }
    }

    /// <summary>Renders actual role choices with native single-selection behavior.</summary>
    public Ui Body => RadioButtons(Profiles.Select(profile => new RadioButtonItem(profile.Name) { Key = profile.Id }))
        .Selected(SelectedIndex)
        .OnSelectionChanged(Select)
        .Palette(Theme.Styles.RadioChoice)
        .Spacing(10f)
        .FontSize(14f)
        .Automation(new()
        {
            Name = Resources.ExternalSelectRole,
            AutomationId = "accounts.external.roles"
        });
}