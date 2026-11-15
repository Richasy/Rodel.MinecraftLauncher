// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Adds a local GGUF directly or discovers models for an online connection; credentials stay in the open form.</summary>
internal sealed partial class AiServiceDialog : Control
{
    private readonly AiConfigurationStore _state;
    private readonly AiServiceFields _fields;
    private readonly AiUiProvider? _existing;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _discoveryCancellation;

    internal AiServiceDialog(
        AiConfigurationStore state,
        Window window,
        Action close,
        AiUiProvider? existing = null,
        bool local = false)
    {
        _state = state;
        Window = window;
        CloseAction = () =>
        {
            if (!Working)
                close();
        };
        _fields = new(this);
        _existing = existing;
        Secret = string.Empty;
        Name = existing?.Name ?? string.Empty;
        Endpoint = existing?.Endpoint ?? string.Empty;
        Code = existing?.Models.FirstOrDefault()?.Code ?? string.Empty;
        ModelName = existing?.Models.FirstOrDefault()?.Name ?? string.Empty;
        Protocol = existing?.Protocol == AiProtocols.OpenAiChat ? 1 : 0;
        Kind = existing?.Protocol switch
        {
            AiProtocols.AnthropicMessages => 1,
            AiProtocols.Gemini => 2,
            AiProtocols.RodelInference => 3,
            _ => local ? 3 : 0
        };
        Error = string.Empty;
        Discovered = [];
        AllowHttp = existing?.AllowHttp ?? false;
        Enabled = existing?.Enabled ?? true;
    }

    internal partial int Step { get; set; }
    internal partial int Kind { get; set; }
    internal partial int Protocol { get; set; }
    internal partial string Name { get; set; }
    internal partial string Endpoint { get; set; }
    internal partial string Secret { get; set; }
    internal partial string Code { get; set; }
    internal partial string ModelName { get; set; }
    internal partial bool Manual { get; set; }
    internal partial bool AllowHttp { get; set; }
    internal partial bool Enabled { get; set; }
    internal partial bool Working { get; private set; }
    internal partial string Error { get; private set; }
    internal partial AiUiModel[] Discovered { get; private set; }
    internal partial int CatalogRevision { get; private set; }
    internal Window Window { get; }
    internal bool NeedsEndpoint => Kind != 3 || _existing is null;

    internal void ChooseLocalFile(string path)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name == System.IO.Path.GetFileNameWithoutExtension(Endpoint))
            Name = System.IO.Path.GetFileNameWithoutExtension(path);
        Endpoint = path;
        Code = path;
    }

    internal void ChangeKind(int kind)
    {
        Kind = kind;
        Endpoint = string.Empty;
        Code = string.Empty;
        ModelName = string.Empty;
        Discovered = [];
        SelectedModels.Clear();
        Manual = false;
        Error = string.Empty;
    }
    internal readonly HashSet<string> SelectedModels = [];

    internal void ToggleModel(string code)
    {
        if (!SelectedModels.Remove(code))
            SelectedModels.Add(code);
        CatalogRevision++;
    }

    internal void EnterModelManually()
    {
        Manual = true;
        Error = string.Empty;
    }

    private bool CanCommit
    {
        get
        {
            _ = CatalogRevision;
            return Code.Trim().Length > 0 || SelectedModels.Count > 0;
        }
    }

    private string ProtocolName => Kind == 0
        ? (Protocol == 0 ? AiProtocols.OpenAiResponses : AiProtocols.OpenAiChat)
        : Kind == 1
        ? AiProtocols.AnthropicMessages
        : Kind == 2 ? AiProtocols.Gemini : AiProtocols.RodelInference;
    internal string KindHelp => Kind switch
    {
        1 => Resources.AiAnthropicHelp,
        2 => Resources.AiGeminiHelp,
        3 => Resources.AiLocalHelp,
        _ => Resources.AiOpenAiHelp
    };
    private Action CloseAction { get; }
    private Action SubmitAction => () => _ = NextAsync();
    private Action PreviousAction => Step == 0 ? CloseAction : ReturnToConnection;
    private bool CanSubmit => !Working && (Kind == 3
        ? !string.IsNullOrWhiteSpace(Endpoint)
        : Step == 0 ? Name.Trim().Length > 0 : CanCommit);
    private static AiDialogAppearance Appearance => AiDialogAppearance.Capture();

    private Ui Body => new AiDialogFrame(
LauncherAppearance.Capture(), Appearance,
        _existing is null ? Resources.AiAddService : Resources.AiEditService,
        _fields,
        CloseAction,
        _existing is not null
            ? Resources.AiSave
            : Kind == 3
            ? Resources.AiAdd
            : Step == 0 ? Resources.AiNext : Resources.AiAdd,
        SubmitAction,
        CanSubmit,
        Math.Min(Step == 0
                ? 620f
                : Manual || Code.Length > 0 ? 600f : 420f, Window.ClientSize.Height - 64f),
        Step == 0 ? Resources.AiCancel : Resources.AiPrevious,
        PreviousAction,
        HeaderAction: Step == 1 ? () => _ = DiscoverAsync() : null,
        HeaderLabel: Resources.AiRefreshModels,
        HeaderAutomationId: "ai.service.refresh",
        HeaderEnabled: !Working);

    private void ReturnToConnection()
    {
        if (Working && _discoveryCancellation is null)
            return;
        _discoveryCancellation?.Cancel();
        Error = string.Empty;
        Step = 0;
    }

    private async Task NextAsync()
    {
        if (Working)
            return;
        Error = string.Empty;
        if (Kind == 3 && _existing is null)
        {
            Working = true;
            var added = await _state.SaveLocalModelAsync(Name, Endpoint);
            if (_lifetime.IsCancellationRequested)
                return;
            Error = _state.Error;
            Working = false;
            if (added)
                CloseAction();
            return;
        }

        if (Step == 0 && _existing is null)
        {
            Step = 1;
            await DiscoverAsync();
            return;
        }

        Working = true;
        var models = Discovered.Where(model => SelectedModels.Contains(model.Code)).ToList();
        if (!string.IsNullOrWhiteSpace(Code))
        {
            models.RemoveAll(model => model.Code == Code.Trim());
            models.Add(new(string.Empty, string.IsNullOrWhiteSpace(ModelName) ? Code.Trim() : ModelName.Trim(), Code.Trim()));
        }

        var saved = await _state.SaveServiceAsync(
            _existing,
            Name.Trim(),
            ProtocolName,
            Endpoint.Trim(),
            Secret,
            [.. models],
            AllowHttp,
            Enabled);
        if (_lifetime.IsCancellationRequested)
            return;
        Error = _state.Error;
        Working = false;
        if (saved)
        {
            Secret = string.Empty;
            CloseAction();
        }
    }

    internal async Task DiscoverAsync()
    {
        if (Working || Kind == 3)
            return;
        using var discovery = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _discoveryCancellation = discovery;
        var cancellation = discovery.Token;
        Working = true;
        Error = string.Empty;
        try
        {
            var result = await AssistantService.DiscoverModelsAsync(new()
            {
                Protocol = ProtocolName,
                BaseUrl = Endpoint.Trim(),
                AllowUnencryptedHttp = AllowHttp
            }, string.IsNullOrEmpty(Secret) ? null : Secret, cancellation);
            if (cancellation.IsCancellationRequested || Step != 1)
                return;
            Discovered = [.. result.Models
                .Select(model => new AiUiModel(string.Empty, model.DisplayName, model.Id))
                .OrderBy(model => model.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(model => model.Code, StringComparer.Ordinal)];
        }
        catch (Exception error)
        {
            if (!cancellation.IsCancellationRequested && Step == 1)
                Error = error.Message;
        }
        finally
        {
            _discoveryCancellation = null;
            if (!_lifetime.IsCancellationRequested)
                Working = false;
        }
    }

    protected override void OnDispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        Secret = string.Empty;
        base.OnDispose();
    }
}