// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Reviews discovered and manually configured models before one atomic selection commit.</summary>
internal sealed partial class AiDiscoveryDialog : Control
{
    private readonly AiConfigurationStore _state;
    private readonly Window _window;
    private readonly AiUiProvider _provider;
    private readonly Action _close;
    private readonly AiDiscoveryFields _fields;
    internal readonly HashSet<string> Selected = [];
    private bool _disposed;

    internal AiDiscoveryDialog(
        AiConfigurationStore state,
        Window window,
        AiUiProvider provider,
        Action close)
    {
        _state = state;
        _window = window;
        _provider = provider;
        _close = close;
        _fields = new(this);
        Models = [.. provider.Models
            .OrderBy(model => model.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(model => model.Code, StringComparer.Ordinal)];
        Error = string.Empty;
        ViewportHeight = window.ClientSize.Height;
        Selected.UnionWith(provider.Models.Select(model => model.Code));
    }

    internal partial AiUiModel[] Models { get; private set; }
    internal partial string Error { get; private set; }
    internal partial bool Working { get; private set; }
    internal partial int Revision { get; private set; }
    private partial float ViewportHeight { get; set; }

    internal void Toggle(string code)
    {
        if (!Selected.Remove(code))
            Selected.Add(code);
        Revision++;
    }

    private static AiDialogAppearance Appearance => AiDialogAppearance.Capture();

    private Ui Body => new AiDialogFrame(
LauncherAppearance.Capture(), Appearance,
        Resources.AiRefreshModels,
        _fields,
        _close,
        Resources.AiSave,
        () => _ = SaveAsync(),
        !Working,
        Math.Min(420f, Math.Max(1f, ViewportHeight - 64f)),
        HeaderAction: () => _ = LoadAsync(),
        HeaderLabel: Resources.AiRefreshModels,
        HeaderAutomationId: "ai.discovery.refresh",
        HeaderEnabled: !Working);

    internal async Task LoadAsync()
    {
        if (Working || _disposed)
            return;
        Working = true;
        Error = string.Empty;
        try
        {
            var discovered = await _state.DiscoverAsync(_provider);
            if (_disposed)
                return;
            Models = [.. _provider.Models
                .Concat(discovered)
                .DistinctBy(model => model.Code)
                .OrderBy(model => model.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(model => model.Code, StringComparer.Ordinal)];
        }
        catch (Exception error)
        {
            if (!_disposed)
                Error = error.Message;
        }
        finally
        {
            if (!_disposed)
                Working = false;
        }
    }

    private async Task SaveAsync()
    {
        if (Working)
            return;
        Working = true;
        var saved = await _state.SelectModelsAsync(_provider, [.. Models.Where(model => Selected.Contains(model.Code))]);
        if (_disposed)
            return;
        Error = _state.Error;
        Working = false;
        if (saved)
            _close();
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs args) => ViewportHeight = args.Size.Height;

    protected override IDisposable? OnMounted()
    {
        _window.SizeChanged += OnSizeChanged;
        _ = LoadAsync();
        return null;
    }

    protected override void OnDispose()
    {
        _disposed = true;
        _window.SizeChanged -= OnSizeChanged;
        base.OnDispose();
    }
}

internal sealed partial class AiDiscoveryFields(AiDiscoveryDialog owner) : Control
{
    private Ui Body => Column([
        Text(Resources.AiDiscoveryNotice)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Automation(new()
            {
                Name = Resources.AiDiscoveryNotice,
                AutomationId = "ai.discovery.notice"
            }),
        InfoBar(message: owner.Error)
            .Severity(InfoBarSeverity.Error)
            .Closable(false)
            .Style(default(InfoBarStyle).CornerRadius(0f).MessageFontSize(12f))
            .Open(owner.Error.Length > 0)
            .Automation(new()
            {
                Name = owner.Error,
                AutomationId = "ai.discovery.error"
            }),
        ItemsRepeater(
            owner.Models,
            static model => model.Code,
            model => new AiDiscoveredModel(owner, model),
            static (row, model) => row.Update(model),
            new StackLayout { EstimatedItemExtent = 72f }).KeyboardNavigation(),
    ])
        .Spacing(12f);
}