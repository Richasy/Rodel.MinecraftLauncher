// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Edits one model identifier in the presentation draft while preserving the other service models.</summary>
internal sealed partial class AiModelDialog : Control
{
    internal readonly AiUiProvider Provider;
    private readonly AiConfigurationStore _state;
    private bool _disposed;
    private readonly AiUiModel? _model;
    private readonly AiModelFields _fields;

    internal AiModelDialog(
        AiConfigurationStore state,
        Window window,
        AiUiProvider provider,
        AiUiModel? model,
        Action close)
    {
        _state = state;
        Window = window;
        Provider = provider;
        _model = model;
        CloseAction = close;
        Name = model?.Name ?? string.Empty;
        Code = model?.Code ?? string.Empty;
        ContextLength = model?.ContextLength;
        DefaultContextLength = AiServiceStore.DefaultLocalContextLength;
        _fields = new(this);
        Error = string.Empty;
        ViewportHeight = window.ClientSize.Height;
    }

    internal partial string Name { get; set; }
    internal partial string Code { get; set; }
    internal partial int? ContextLength { get; private set; }
    internal partial int? DeclaredContextLength { get; private set; }
    internal partial int DefaultContextLength { get; private set; }
    internal int EffectiveContextLength => ContextLength ?? DefaultContextLength;
    internal int MaximumContextLength => Math.Max(DeclaredContextLength ?? 131072, EffectiveContextLength);
    internal int MinimumContextLength => Math.Min(1024, MaximumContextLength);
    internal string ContextValue => string.Format(ContextLength is null ? Resources.AiContextAutomaticValue : Resources.AiContextValue, EffectiveContextLength);
    internal string ContextHelp => DeclaredContextLength is { } length ? string.Format(Resources.AiContextDeclaredHelp, length) : Resources.AiContextUnknownHelp;
    private int _metadataGeneration;

    internal void SetContext(double value) => ContextLength = Math.Clamp((int)Math.Round(value / 1024) * 1024, MinimumContextLength, MaximumContextLength);

    internal void ResetContext() => ContextLength = null;

    internal void SetPath(string path)
    {
        Code = path;
        _ = InspectAsync();
    }

    private async Task InspectAsync()
    {
        var generation = ++_metadataGeneration;
        var info = await AiServiceStore.InspectLocalModelAsync(Code);
        if (generation != _metadataGeneration)
            return;
        DeclaredContextLength = info.DeclaredContextLength;
        DefaultContextLength = info.DefaultContextLength;
    }

    internal partial string Error { get; private set; }
    internal partial bool Working { get; private set; }
    internal Window Window { get; }
    internal bool Local => Provider.Protocol == AiProtocols.RodelInference;
    private partial float ViewportHeight { get; set; }
    // Two fields need a compact body; failures add reading space without moving the fixed actions out of view.
    private float DialogHeight => Math.Min((Local ? 488f : 312f) + (Error.Length > 0 ? 72f : 0f), Math.Max(0f, ViewportHeight - 64f));
    private Action SaveAction => () => _ = SaveAsync();
    private Action CloseAction { get; }
    private static AiDialogAppearance Appearance => AiDialogAppearance.Capture();

    private Ui Body => new AiDialogFrame(
LauncherAppearance.Capture(), Appearance,
        _model is null ? Resources.AiAddServiceModel : Resources.AiEditModel,
        _fields,
        CloseAction,
        Resources.AiSave,
        SaveAction,
        !Working && Code.Trim()
            .Length > 0,
        DialogHeight);

    private async Task SaveAsync()
    {
        if (Working)
            return;
        Working = true;
        var saved = await _state.SaveModelAsync(
            Provider,
            _model,
            string.IsNullOrWhiteSpace(Name)
                ? Local ? System.IO.Path.GetFileNameWithoutExtension(Code.Trim()) : Code.Trim()
                : Name.Trim(),
            Code.Trim(),
            Local ? ContextLength : null,
            Local && ContextLength is null);
        if (_disposed)
            return;
        Error = _state.Error;
        Working = false;
        if (saved)
            CloseAction();
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs args) => ViewportHeight = args.Size.Height;

    protected override IDisposable? OnMounted()
    {
        Window.SizeChanged += OnSizeChanged;
        if (Local)
            _ = InspectAsync();
        return null;
    }

    protected override void OnDispose()
    {
        _disposed = true;
        Window.SizeChanged -= OnSizeChanged;
        _metadataGeneration++;
        base.OnDispose();
    }
}

internal sealed partial class AiModelFields(AiModelDialog owner) : Control
{
    private Ui Body => Column([
        Text(owner.Error)
            .FontSize(12f).Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(owner.Error.Length > 0),
        Text(Resources.AiDisplayName)
            .FontSize(13f).Foreground(LauncherTheme.Ink),
        TextBox(owner.Name, value => owner.Name = value)
            .Style(LauncherStyles.Input)
            .Automation(new()
            {
                Name = Resources.AiDisplayName,
                AutomationId = "ai.model.name"
            }),
        Text(Resources.AiModelNameHelp)
            .FontSize(11f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(!owner.Local),
        Text(owner.Local ? Resources.AiLocalFile : Resources.AiModelIdentifier)
            .FontSize(13f).Foreground(LauncherTheme.Ink),
        new FileSystemPathInput(
            owner.Window,
            owner.Code,
            value => owner.SetPath(value),
            Resources.AiLocalFile,
            "ai.model.path",
            Enabled: !owner.Working,
            Extensions: [".gguf"],
            Placeholder: Resources.AiLocalFilePlaceholder).Visible(owner.Local),
        TextBox(owner.Code, value => owner.Code = value)
            .Style(LauncherStyles.Input)
            .Visible(!owner.Local)
            .Automation(new()
            {
                Name = Resources.AiModelIdentifier,
                AutomationId = "ai.model.code"
            }),
        Column([
            Row([
                Text(Resources.AiLocalContextLength)
                    .FontSize(13f).Foreground(LauncherTheme.Ink)
                    .Weight(1f),
                Text(owner.ContextValue)
                    .FontSize(12f).Foreground(LauncherTheme.Ink),
            ])
                .Spacing(12f),
            new LauncherSlider(
                owner.EffectiveContextLength,
                value => owner.SetContext(value),
                owner.MinimumContextLength,
                owner.MaximumContextLength,
                1024,
                8192,
                Resources.AiLocalContextLength,
                "ai.model.context",
                !owner.Working),
            Text(owner.ContextHelp)
                .FontSize(11f).Foreground(LauncherTheme.Muted)
                .Wrap(),
            LauncherControls.OutlineCommand(Resources.AiContextReset, () => owner.ResetContext())
                .Style(LauncherButtons.DialogSecondary)
                .Enabled(!owner.Working && owner.ContextLength is not null)
                .Automation(new()
                {
                    Name = Resources.AiContextReset,
                    AutomationId = "ai.model.context.reset"
                }),
        ])
            .Spacing(8f)
            .Visible(owner.Local),
    ])
        .Spacing(8f);
}