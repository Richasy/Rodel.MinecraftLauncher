// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Markdown;
using Sprout.Reconcile;
using Sprout.SyntaxHighlighting;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Hosts native selectable Markdown with append streaming and theme-following code colors.</summary>
/// <param name = "Source">The actual provider content to render.</param>
/// <param name = "Appearance">Current theme and typography, resolved by the owning control.</param>
/// <param name = "CodeSurface">Current theme's code reading surface.</param>
/// <param name = "Ink">Current theme's code foreground.</param>
/// <param name="Streaming">Whether this document can still receive appended model output.</param>
/// <param name="Id">The actual document's optional automation identity inside a conditional branch.</param>
[View]
internal readonly partial record struct AiMarkdownView(
    string Source,
    MarkdownStyle Appearance,
    Color CodeSurface,
    Color Ink,
    bool Streaming = false,
    string? Id = null)
{
    internal static readonly Func<string, string?, IReadOnlyList<RunSpan>> CodeHighlighter = HighlightRunBuilder.MarkdownRuns();
    internal static readonly Func<string, string, Control> CodeHeader = static (language, code) => new AiCodeHeader(language, code);
    internal static readonly Func<string, ImageSource?> SkipImage = _ => null;
    private static readonly Dictionary<(LauncherAppearance Theme, bool Thinking), MarkdownStyle> Styles = [];

    internal static MarkdownStyle GetStyle(bool thinking)
    {
        var theme = LauncherAppearance.Capture();
        var key = (theme, thinking);
        if (!Styles.TryGetValue(key, out var style))
        {
            style = new MarkdownStyle
            {
                FontFamily = "Microsoft YaHei UI",
                FontSize = thinking ? 12f : 14f,
                Foreground = thinking ? theme.Muted : theme.Ink,
                MonospaceFontFamily = "Consolas",
                BlockSpacing = thinking ? 10f : 14f,
                Paragraph = new(LineHeight: thinking ? 20f : 25f),
                Heading1 = new(FontSize: thinking ? 20f : 24f, LineHeight: 32f, Margin: new Thickness(0f, 16f, 0f, 12f)),
                Heading2 = new(FontSize: thinking ? 18f : 21f, LineHeight: 29f, Margin: new Thickness(0f, 14f, 0f, 10f)),
                Heading3 = new(FontSize: thinking ? 16f : 18f, LineHeight: 26f, Margin: new Thickness(0f, 12f, 0f, 8f)),
                Heading4 = new(FontSize: 15f, Margin: new Thickness(0f, 10f, 0f, 6f)),
                List = new(Indent: 22f, ItemSpacing: 6f),
                InlineCode = new(FontSize: thinking ? 11f : 13f, Foreground: theme.Ink),
                Quote = new(Foreground: theme.Muted, BarBrush: theme.Edge, BarWidth: 3f, Padding: new Thickness(14f, 6f, 8f, 6f)),
                Rule = new(Brush: theme.Edge, Margin: new Thickness(0f, 8f, 0f, 18f)),
                Table = new(
                    CellPadding: new Thickness(12f, 10f, 12f, 10f),
                    BorderBrush: theme.Edge,
                    BorderThickness: .5f,
                    HeaderBackground: theme.AiUserMessageBackground,
                    HeaderForeground: theme.Ink,
                    OddRowBackground: theme.ControlBackground,
                    EvenRowBackground: theme.HighContrast ? theme.Background : theme.ControlBackground.MultiplyAlpha(.35f),
                    Layout: MarkdownTableLayout.Fill,
                    MinColumnWidth: 96f,
                    Margin: new Thickness(0f, 4f, 0f, 18f)),
                CodeBlock = new(
                    FontSize: 12f,
                    BorderBrush: theme.Edge,
                    BorderThickness: 1f,
                    CornerRadius: 0f,
                    Padding: new Thickness(14f)),
                TaskCheckbox = new(CornerRadius: 0f),
                Alert = new(CornerRadius: 0f),
            };
            Styles[key] = style;
        }

        return style;
    }

    /// <summary>Builds the native document; model image destinations do not authorize local-file reads.</summary>
    public Ui Body => Column([
        new MarkdownTextBlock(Source).Streaming(Streaming)
        .Style(Appearance)
        .Highlighter(CodeHighlighter)
        .CodeBackground(CodeSurface)
        .CodeForeground(Ink)
        .CodeBlockHeader(CodeHeader)
        .ImageResolver(SkipImage)
        .OnLinkActivated(url =>
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
                _ = Windows.System.Launcher.LaunchUriAsync(uri);
        })
        .Automation(new() { AutomationId = Id }),
    ]);
}