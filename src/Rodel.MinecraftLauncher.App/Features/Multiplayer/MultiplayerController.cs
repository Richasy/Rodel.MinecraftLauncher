// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Features.Packs;
using Rodel.MinecraftLauncher.App.Features.Tasks;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Multiplayer;
using Rodel.MinecraftLauncher.Core.Packs;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.App.Features.Multiplayer;

/// <summary>Retains a room draft and observes Core tasks while the page or its dialog is closed.</summary>
internal sealed class MultiplayerController : IDisposable
{
    private readonly LauncherClient _client;
    private readonly HomeController _home;
    private readonly Func<Task<LaunchOptions>> _defaults;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly FeatureTaskController _reviews;
    private string? _jobId;
    private bool _started;
    private bool _nameEdited;
    private string _name = string.Empty;
    private (string Instance, string Epoch, string Name)? _worldImageKey;
    private DateTimeOffset _nextWorldImageRead;
    private Task? _worldImageRead;
    internal string? WorldIconData { get; private set; }

    internal Action? ReturnToGame { get; set; }

    internal Features.Create.CreateController Create { get; }

    internal MultiplayerDraftChoices Choices { get; }

    internal PackImportController Pack { get; }

    internal MultiplayerRoom? Room { get; private set; }

    internal string Role { get; private set; } = "host";
    internal string Stage { get; private set; } = "room";
    internal string InstanceId { get; private set; } = string.Empty;
    internal string Name { get => _name; set { if (_name != value) { _name = value; _nameEdited = true; Notify(); } } }

    internal bool HasRoomName => !string.IsNullOrWhiteSpace(Name);
    internal bool CanConnect => !Preparing && _jobId is null && Room?.Active != true && Choices.CanLaunch && InstanceId.Length > 0 && (Role == "host" ? HasRoomName : RoomCode.Parse(Code) is not null);
    internal string Code { get; set { if (field != value) { field = value; Notify(); } } } = string.Empty;
    internal string TargetVersion { get; set { if (field != value) { field = value; Notify(); } } } = string.Empty;
    internal string Error { get; private set; } = string.Empty;
    internal bool Busy { get; private set; }

    internal bool Preparing => Busy || Create.State.Active || Pack.State.Active || _reviews.Active;
    internal bool CanCancelPreparation => Create.State.Active || Pack.State.Active || _reviews.Active;
    internal bool PreparationCancelRequested => _reviews.Active ? _reviews.CancelRequested : Create.State.Active ? Create.State.CancelRequested : Pack.State.CancelRequested;
    internal bool PreparingPackUpdate => _reviews.Active;
    internal bool PreparationCancellationFailed => _reviews.Active ? !_reviews.CancelRequested && _reviews.Error.Length > 0 : Create.State.Active ? !Create.State.CancelRequested && Create.State.Error.Length > 0 : Pack.State.Active && !Pack.State.CancelRequested && Pack.State.Error.Length > 0;
    internal bool GameRunning { get; private set; }

    internal string GameActionLabel => GameRunning ? Resources.MultiplayerReturnGame : Busy ? Resources.LaunchPreparing : Resources.CreateLaunchGame;
    private MultiplayerGameAction GameAction => MultiplayerPresentation.GameAction(Room, GameRunning, Preparing);
    internal bool CanOpenGame => GameAction != MultiplayerGameAction.Unavailable;
    internal bool HasOwnedWork => Busy || _jobId is not null || Create.State.Active || Pack.State.Active || _reviews.Active || (Room is { Active: true, OwnerProcessId: var pid } && pid == Environment.ProcessId);
    internal Dictionary<string, string> SelectedFiles { get; } = [];
    internal IReadOnlyList<InstanceSummary> Instances => [.. _home.State.LocalInstances.Where(item => !item.Unreadable)];
    internal PackReview? Review { get; private set; }

    internal string Progress => _reviews.Active ? _reviews.Progress : Create.State.Active ? Create.State.PreparationProgress : Pack.State.Progress;
    internal event Action? Changed;

    internal MultiplayerController(LauncherClient client, HomeController home, Func<Task<LaunchOptions>> defaults)
    {
        _client = client; _home = home; _defaults = defaults;
        Choices = new(client, OnChoicesChanged, _lifetime.Token);
        Create = new(client); Pack = new(client, home); _reviews = new(client, home);
        Create.StateChanged += OnCreateChanged; Create.Completed += OnCreated;
        Pack.Changed += OnPackChanged; _reviews.Changed += OnReviewChanged;
        home.StateChanged += OnHomeChanged;
    }

    internal void Start()
    {
        if (_started) return;
        _started = true;
        _ = ObserveAsync();
    }

    internal void Open(string role, string? instanceId = null)
    {
        if (Role != role && !HasOwnedWork)
        {
            Role = role; Stage = "room"; _name = Code = TargetVersion = string.Empty; _nameEdited = false; Review = null;
            Pack.ResetDraft(); SelectedFiles.Clear(); Create.StartNew();
        }

        if (instanceId is not null) InstanceId = instanceId;
        if (InstanceId.Length == 0 && Room is { } room) InstanceId = room.InstanceId;
        if (!Instances.Any(item => item.Id == InstanceId)) InstanceId = Instances.FirstOrDefault(item => item.Id == _home.State.SelectedId)?.Id ?? (Instances.Count > 0 ? Instances[0].Id : null) ?? string.Empty;
        Error = string.Empty; Notify();
        _ = Choices.LoadAsync(InstanceId);
    }

    internal void SelectInstance(int index)
    {
        if (index >= 0 && index < Instances.Count && InstanceId != Instances[index].Id)
        { InstanceId = Instances[index].Id; Review = null; _ = Choices.LoadAsync(InstanceId); Notify(); }
    }

    private void OnChoicesChanged()
    {
        if (!_nameEdited && Role == "host") _name = Choices.CurrentWorldName ?? Choices.World?.Name ?? Instances.FirstOrDefault(item => item.Id == InstanceId)?.DisplayName ?? string.Empty;
        if (Choices.Error.Length > 0) Error = Choices.Error;
        Notify();
    }

    internal void ShowStage(string stage)
    {
        if (Busy || Create.State.Active || Pack.State.Active || _reviews.Active) return;
        Stage = stage; Error = string.Empty;
        if (stage == "pack" && Pack.State.Completed) { Pack.ResetDraft(); SelectedFiles.Clear(); }

        if (stage is "create" or "target") { Create.Open(fresh: true); }

        Notify();
    }

    internal async Task CreateInstanceAsync()
    {
        if (Role == "guest")
        {
            if (string.IsNullOrWhiteSpace(TargetVersion)) { Error = Resources.MultiplayerTargetRequired; Notify(); return; }

            Create.ChangeVersionText(TargetVersion.Trim()); Create.ValidateVersion();
        }

        await Create.ConfirmAsync();
    }

    private void OnCreateChanged(Features.Create.CreateState _)
    {
        if (Role == "guest" && Stage == "create" && TargetVersion.Length > 0 && !Create.State.Active && !Create.State.Loading && Create.State.VersionText != TargetVersion)
            Create.ChangeVersionText(TargetVersion);
        Notify();
    }

    private async void OnCreated(string? id)
    {
        if (id is not null && Create.State.Succeeded) { InstanceId = id; Stage = "room"; await _home.RefreshAsync(id); await Choices.LoadAsync(id); }

        Notify();
    }

    private void OnPackChanged(PackImportState state)
    {
        if (state.RetainedInstanceId.Length > 0 && InstanceId != state.RetainedInstanceId)
        { InstanceId = state.RetainedInstanceId; _ = Choices.LoadAsync(InstanceId); }

        if (state.Completed && !state.Active) Stage = "room";
        Notify();
    }

    private void OnReviewChanged() { if (_reviews.Error.Length > 0) Error = _reviews.Error; Notify(); }

    private void OnHomeChanged(HomePageState _) => Notify();
    internal void Notify() { if (!_lifetime.IsCancellationRequested) Changed?.Invoke(); }

    internal async Task ConnectAsync()
    {
        if (!CanConnect || _jobId is not null || Room?.Active == true) return;
        if ((InstanceId.Length == 0) || ((Role == "guest") && RoomCode.Parse(Code) is null))
        { Error = InstanceId.Length == 0 ? Resources.MultiplayerSelectInstance : Resources.MultiplayerCodeInvalid; Notify(); return; }

        Busy = true; Error = string.Empty; Notify();
        try
        {
            var account = _home.RequireLaunchIdentity is { } require ? await require(_lifetime.Token) : null;
            if (account is null) return;
            var launch = await _defaults();
            var request = new MultiplayerRequest
            {
                RoomId = Guid.NewGuid().ToString("N"),
                Role = Role,
                InstanceId = InstanceId,
                Name = Name,
                PlayerName = _client.Identities.Get(account).Name ?? string.Empty,
                Invitation = Role == "guest" ? Code : null,
                SessionId = Choices.SessionId,
                StartGame = Role == "guest",
                RestartGame = Role == "guest" && Choices.HasGame,
                Launch = launch with { AccountId = account, InheritIdentity = true, World = Role == "host" && !Choices.HasGame ? Choices.WorldFolder ?? string.Empty : string.Empty }
            };
            var job = await Task.Run(() => _client.Tasks.StartAsync(new MultiplayerTask(request), _lifetime.Token));
            _jobId = job.Id;
        }
        catch (Exception error) { Error = error.Message; }
        finally { Busy = false; Notify(); }
    }

    internal async Task ControlAsync(string action, string? value = null)
    {
        if (Room is not { } room || Busy) return;
        Busy = true; Error = string.Empty; Notify();
        try
        {
            await Task.Run(() => action == "invitation"
                ? _client.Multiplayer.ChangeInvitationAsync(room.Id, value ?? Code, _lifetime.Token)
                : _client.Multiplayer.ControlAsync(room.Id, action, value, _lifetime.Token));
        }
        catch (Exception error) { Error = error.Message; }
        finally { Busy = false; Notify(); }
    }

    internal async Task EndAsync(bool exitGame)
    {
        if (Room is not { } room || Busy) return;
        Busy = true; Error = string.Empty; Notify();
        try { await Task.Run(() => _client.Multiplayer.EndAsync(room.Id, exitGame ? MultiplayerEndMode.ExitGame : MultiplayerEndMode.KeepGame, _lifetime.Token)); }
        catch (Exception error) { Error = error.Message; }
        finally { Busy = false; Notify(); }
    }

    internal Task<MultiplayerRoom?> ReadRoomAsync() => Task.Run(_client.Multiplayer.Get, _lifetime.Token);

    internal async Task StopForFeatureAsync(string roomId)
    {
        Busy = true; Error = string.Empty; Notify();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var room = await Task.Run(_client.Multiplayer.Get, deadline.Token);
            if (room is null) return;
            if (room.Id != roomId || room.State is "ending" or "exit-pending")
                throw new InvalidOperationException(Resources.MultiplayerStopPending);

            await Task.Run(() => _client.Multiplayer.EndAsync(roomId, MultiplayerEndMode.KeepGame, deadline.Token));
            while (true)
            {
                room = await Task.Run(_client.Multiplayer.Get, deadline.Token);
                if (room is null) return;
                if (room.Id != roomId) throw new InvalidOperationException(Resources.MultiplayerStopPending);
                if (!room.Active)
                {
                    if (room.State == "interrupted")
                        await Task.Run(() => _client.Multiplayer.EndAsync(roomId, MultiplayerEndMode.KeepGame, deadline.Token));
                    return;
                }

                await Task.Delay(150, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
        {
            throw new InvalidOperationException(Resources.MultiplayerStopPending);
        }
        finally { Busy = false; Notify(); }
    }

    internal async Task OpenGameAsync()
    {
        var action = GameAction;
        if (action == MultiplayerGameAction.Unavailable || Room is not { } room) return;
        if (action == MultiplayerGameAction.ReturnToGame) { ReturnToGame?.Invoke(); return; }

        if (action == MultiplayerGameAction.LaunchGuest) { await ControlAsync("launch"); return; }

    }

    internal async Task ReviewPackAsync(string source)
    {
        if (Preparing || InstanceId.Length == 0) return;
        Review = null;
        await _reviews.RunAsync(new ReviewPackUpdateTask(InstanceId, source, new()));
        if (_reviews.Job?.Result is { } result) Review = result.Deserialize(PackJsonContext.Default.PackReview);
        Error = _reviews.Error; Notify();
    }

    internal async Task ApplyPackAsync()
    {
        if (Review is not { } review) return;
        await _reviews.RunAsync(new ApplyPackUpdateTask(review.OperationId, new()), InstanceId);
        Error = _reviews.Error;
        if (_reviews.Outcome == "succeeded") { Review = null; Stage = "room"; }

        Notify();
    }

    internal async Task StageFileAsync(PackMissingFile missing, string source)
    {
        if (Pack.State.RetainedInstanceId.Length == 0) return;
        try
        {
            var staged = await _client.Packs.StageManualFileAsync(Pack.State.RetainedInstanceId, missing.ManualKey ?? missing.Path!, source, _lifetime.Token);
            Pack.SetManualDirectory(staged.ManualDirectory);
            SelectedFiles[staged.Key] = staged.FileName;
        }
        catch (Exception error) { Error = error.Message; }

        Notify();
    }

    internal Task CancelPreparationAsync() => _reviews.Active ? _reviews.CancelAsync() : Create.State.Active ? Create.CancelAsync() : Pack.CancelAsync();
    internal void ReportError(string message) { Error = message; Notify(); }

    private async Task ObserveAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                var room = await Task.Run(_client.Multiplayer.Get, _lifetime.Token);
                var gameRunning = await Task.Run(() => ObserveRoomGame(room), _lifetime.Token);
                var changed = !MultiplayerPresentation.Equivalent(room, Room) || GameRunning != gameRunning;
                Room = room; GameRunning = gameRunning;
                var imageKey = room is { Role: "host", World.Name.Length: > 0 }

                    ? (room.InstanceId, room.World.Epoch, room.World.Name) : ((string, string, string)?)null;
                if (_worldImageKey != imageKey)
                {
                    _worldImageKey = imageKey; WorldIconData = null; _nextWorldImageRead = DateTimeOffset.MinValue; changed = true;
                }

                if (changed) Notify();
                if (_worldImageRead?.IsCompleted != false)
                    _worldImageRead = ReadWorldImageAsync();
                if (_jobId is { } id)
                {
                    var job = await Task.Run(() => _client.Jobs.Get(id), _lifetime.Token);
                    if (job.Status is "failed" or "canceled" or "interrupted" or "succeeded")
                    { _jobId = null; Error = job.Error ?? string.Empty; Notify(); }
                }

                await Task.Delay(500, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                Error = error.Message; Notify();
                try { await Task.Delay(1000, _lifetime.Token); }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { break; }
            }
        }
    }

    private bool ObserveRoomGame(MultiplayerRoom? room)
    {
        if (room?.SessionId is not { } id) return false;
        try
        {
            var game = _client.Sessions.Get(id);
            return game.Status == "running" && game.Readiness == "window_visible" && game.ActivityUnknown != true
                && game.InstanceId == room.InstanceId && game.ProcessId == room.GameProcessId && game.ProcessStartUtc == room.GameStartedAt;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
        { return false; }
    }

    private async Task ReadWorldImageAsync()
    {
        if (_worldImageKey is not { } key || WorldIconData is not null || DateTimeOffset.UtcNow < _nextWorldImageRead)
            return;
        _nextWorldImageRead = DateTimeOffset.UtcNow.AddSeconds(5);
        var token = _lifetime.Token;
        try
        {
            var bytes = await Task.Run(async () =>
            {
                var worlds = await _client.Worlds.ListAsync(key.Instance, token).ConfigureAwait(false);
                // Use verified level metadata only when the observed name identifies exactly one save.
                // Duplicate display names must not select an arbitrary folder or thumbnail.
                var matches = worlds.Where(world => world.Warning is null && world.Name == key.Name).Take(2).ToArray();
                return matches is [{ Icon: { } file }]
                    ? await Features.Content.WorldIconReader.ReadAsync(file, token).ConfigureAwait(false) : null;
            }, token);
            if (!token.IsCancellationRequested && _worldImageKey == key && bytes is not null)
            {
                WorldIconData = Convert.ToBase64String(bytes); Notify();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // An optional thumbnail never changes room availability or blocks an otherwise usable save.
        }
    }

    /// <summary>Releases this window's observers and draft resources; external room owners remain independent.</summary>
    public void Dispose()
    {
        _lifetime.Cancel(); _home.StateChanged -= OnHomeChanged;
        Create.StateChanged -= OnCreateChanged; Create.Completed -= OnCreated; Pack.Changed -= OnPackChanged;
        _reviews.Changed -= OnReviewChanged; Create.Dispose(); Pack.Dispose(); _reviews.Dispose(); _lifetime.Dispose();
    }
}