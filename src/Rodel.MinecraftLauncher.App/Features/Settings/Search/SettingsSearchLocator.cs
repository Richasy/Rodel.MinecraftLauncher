// Copyright (c) Richasy. All rights reserved.

using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Semantics;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

/// <summary>Resolves the actual semantic target to its interactive descendant after layout.</summary>
internal static class SettingsSearchLocator
{
    internal static InteractionNode? Find(SemanticsNode semantics, InteractionNode interactions, string id)
    {
        SemanticsNode? Locate(SemanticsNode node)
        {
            if (node.AutomationId == id)
                return node;
            foreach (var child in node.Children)
            {
                if (Locate(child) is { } match)
                    return match;
            }

            return null;
        }

        if (Locate(semantics) is not { } row)
            return null;
        var ids = new HashSet<ElementId>();
        void Collect(SemanticsNode node)
        {
            ids.Add(node.StableId);
            foreach (var child in node.Children)
                Collect(child);
        }

        Collect(row);
        InteractionNode? FindInteraction(InteractionNode node)
        {
            if (node.IsCollapsed)
                return null;
            if ((node.Focusable || !node.IsEffectivelyEnabled) && ids.Contains(node.StableId))
                return node;
            foreach (var child in node.Children)
            {
                if (FindInteraction(child) is { } match)
                    return match;
            }

            return null;
        }

        return FindInteraction(interactions);
    }
}