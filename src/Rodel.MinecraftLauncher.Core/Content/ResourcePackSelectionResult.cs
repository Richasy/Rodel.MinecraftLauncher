// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Configuration;
using System.Diagnostics.CodeAnalysis;

namespace Rodel.MinecraftLauncher.Core.Content;

/// <summary>Reports the actual settings change together with the requested resource-pack order and advisory warnings.</summary>
public sealed record ResourcePackSelectionResult : GameSettingsChangeResult
{
    /// <summary>Retains the settings operation's complete outcome and adds the selected pack identities.</summary>
    /// <param name="settings">The directly executed or planned settings change.</param>
    /// <param name="selected">The final selected resource-pack identities in order.</param>
    /// <param name="warnings">Selection guidance that does not block explicitly requested entries.</param>
    [SetsRequiredMembers]
    public ResourcePackSelectionResult(GameSettingsChangeResult settings, IReadOnlyList<string> selected, IReadOnlyList<string> warnings)
        : base(settings)
    {
        Selected = selected;
        Warnings = warnings;
    }

    /// <summary>Gets the final selection order, including explicitly requested unfamiliar identifiers.</summary>
    public IReadOnlyList<string> Selected { get; }
    /// <summary>Gets nonblocking selection guidance.</summary>
    public IReadOnlyList<string> Warnings { get; }
}