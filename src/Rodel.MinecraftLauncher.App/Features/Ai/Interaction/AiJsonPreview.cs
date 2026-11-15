// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Graphics;
using Sprout.Reconcile;
using Sprout.SyntaxHighlighting;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Displays selectable, formatted JSON with theme-following syntax colors on a separate code surface.</summary>
[View]
internal readonly partial record struct AiJsonPreview(LauncherAppearance Theme, string Json, string Id)
{
    private static readonly HighlightRunBuilder Highlighter = new();
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        TypeInfoResolver = AiJsonContext.Default
    };
    internal string Formatted
    {
        get
        {
            try
            {
                return JsonNode.Parse(Json)?.ToJsonString(Options) ?? Json;
            }
            catch (JsonException)
            {
                return Json;
            }
        }
    }

    internal TextView Code => Text(Formatted).FontFamily("Consolas").FontSize(12f).Foreground(Theme.Ink).Wrap() with
    {
        Runs = Highlighter.Build(Formatted, "json").ToArray()
    };

    /// <summary>Builds one rich-text selection surface rather than a separate control for each token.</summary>
    public Ui Body => Border(Flow(FlowDirection.LeftToRight, SelectableText(Code)
                .Automation(new() { AutomationId = Id })))
        .Padding(14f)
        .Background(Theme.ControlBackground)
        .BorderBrush(Theme.Edge)
        .BorderThickness(1f);
}