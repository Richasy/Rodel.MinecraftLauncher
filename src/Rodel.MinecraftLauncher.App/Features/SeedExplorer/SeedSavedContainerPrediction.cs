// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Worlds.Maps;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Explains absent saved items and displays an explicitly requested prediction in its own inspector state.</summary>
internal sealed partial class SeedSavedContainerPrediction : Control
{
    private SeedExplorerState _state;
    private readonly Action _performAction;
    private SeedLootPredictionController? _prediction;
    private bool _disposed;
    internal SeedSavedContainerPrediction(SeedExplorerState state, SeedContainer container, bool region)
    {
        _state = state; _performAction = PerformAction; Container = container; Region = region; Images = new Dictionary<string, ImageSource>();
    }
    private partial SeedContainer Container { get; set; }
    private partial bool Region { get; set; }
    private partial IReadOnlyDictionary<string, ImageSource> Images { get; set; }
    private SeedExplorerSnapshot Current => _state.Current;
    private bool SavedContext => Current.UseSavedMap && (Region || !Current.ScenePreview);
    private WorldMapSnapshot? ContextSnapshot => Current.WorldSnapshot is { } snapshot && Matches(snapshot, Current.World.Dimension, Container) ? snapshot : null;
    private SeedLootPredictionController Prediction => _prediction ??= CreatePrediction();
    private SeedLootPredictionStatus Status => Prediction.Current;
    private string Reason => string.IsNullOrWhiteSpace(Container.LootTable) ? Resources.SeedSavedLootNoTable
        : string.Format(Resources.SeedSavedLootTableFormat, Container.LootTable) + (Current.WorldSnapshot?.Seed is null ? Environment.NewLine + Resources.SeedLootPredictionNeedsSeed : string.Empty);
    private string NoticeTitle => Status.Busy ? Status.Canceling ? Resources.SeedLootPredictionCanceling : Resources.SeedLootPredictionBusy
        : Status.Error.Length > 0 ? Resources.SeedLootPredictionFailed : Status.Result is not null ? Resources.SeedLootPredictionResult : Resources.SeedSavedLootIncomplete;
    private string NoticeDetail => Status.Error.Length > 0 ? string.Format(Resources.SeedLootPredictionFailureFormat, Status.Error)
        : Status.Result is null ? Reason : Resources.SeedLootPredictionContext
            + (Status.Result.Deterministic ? string.Empty : Environment.NewLine + Resources.SeedLootPredictionUnfixed)
            + (Warnings.Length == 0 ? string.Empty : Environment.NewLine + Warnings);
    private string ItemLabel => Status.Selected is { } item ? string.Format(Resources.SeedItemCountFormat, SeedRegionText.Name(Current, item.Id, true), item.Count) : Resources.SeedSelectItem;
    private string ItemDetails => Status.Selected is { } item ? SeedRegionText.Components(Current, item) : string.Empty;
    private string Warnings => Status.Result is { Warnings.Length: > 0 } result ? string.Format(Resources.SeedLootPredictionWarningsFormat, string.Join(Environment.NewLine, result.Warnings)) : string.Empty;
    private SeedInventorySlot[] Slots => Status.Result is not { Container: { } container } ? []
        : [.. Enumerable.Range(0, container.Slots).Select(index =>
        {
            var item = container.Items.FirstOrDefault(value => value.Slot == index);
            return new SeedInventorySlot(index, item, item is null ? null : Images.GetValueOrDefault(item.Id) ?? Current.ItemImages.GetValueOrDefault(item.Id), item is not null && item == Status.Selected,
                item is null ? Resources.SeedEmptySlot : string.Format(Resources.SeedItemCountFormat, SeedRegionText.Name(Current, item.Id, true), item.Count));
        })];

    private Ui Body => Column([
        new LauncherNotice(LauncherAppearance.Capture(),NoticeTitle, NoticeDetail, Status.Error.Length > 0 ? LauncherNoticeKind.Error : Status.Result is not null ? LauncherNoticeKind.Success : LauncherNoticeKind.Information, "seed.loot.prediction.notice",
            Status.Busy ? Resources.DialogCancel : Resources.SeedPredictLoot, _performAction, Status.Busy ? !Status.Canceling : Prediction.CanPredict),
        Column([
            ItemsRepeater(Slots, slot => slot.Index, slot => new SeedInventorySlotView(LauncherAppearance.Capture(),slot, () => Prediction.Select(slot.Item), "loot.prediction"), new SeedInventoryLayout()).KeyboardNavigation(),
            Text(Resources.SeedLootPredictionEmpty).FontSize(11f).Foreground(LauncherTheme.Muted).Visible(Status.Result?.Container.Items.Length == 0),
            Border(Column([
                Text(ItemLabel).FontSize(12f).Wrap(),
                Text(ItemDetails).FontSize(11f).Foreground(LauncherTheme.Muted).Wrap().Visible(ItemDetails.Length > 0),
            ]).Spacing(6f)).Padding(10f).Background(LauncherTheme.ControlBackground).Visible(Status.Selected is not null),
        ]).Spacing(8f).Visible(Status.Result is not null),
    ]).Spacing(8f);

    private SeedLootPredictionController CreatePrediction()
    {
        var controller = new SeedLootPredictionController(PredictAsync, Matches, _state.Controller.Lifetime.Token);
        controller.SetContext(ContextSnapshot, Current.World.Dimension, Container);
        return controller;
    }
    private void PerformAction() { if (Status.Busy) Prediction.Cancel(); else _ = Prediction.StartAsync(); }
    private bool Matches(WorldMapSnapshot snapshot, string dimension, SeedContainer recorded)
    {
        var selected = Region ? Current.RegionContainer : Current.SceneContainer;
        return !_disposed && SavedContext && Current.WorldSnapshot?.Selection == snapshot.Selection && Current.WorldSnapshot?.Revision == snapshot.Revision
            && Current.World.Dimension == dimension && selected is not null && selected.Id == recorded.Id && selected.X == recorded.X && selected.Y == recorded.Y && selected.Z == recorded.Z
            && selected.State == recorded.State && selected.LootTable == recorded.LootTable && selected.LootSeed == recorded.LootSeed;
    }
    private async Task<WorldContainerPrediction> PredictAsync(WorldMapSnapshot snapshot, string dimension, SeedContainer recorded, CancellationToken ct)
    {
        var result = await _state.Controller.Client.WorldMaps.PredictContainerAsync(snapshot, dimension, recorded, ct);
        ct.ThrowIfCancellationRequested();
        if (!Matches(snapshot, dimension, recorded)) return result;
        try
        {
            var images = await _state.Controller.Map.ItemImagesAsync(result.Container.Items.Select(item => item.Id), ct);
            ct.ThrowIfCancellationRequested();
            if (Matches(snapshot, dimension, recorded)) Images = images.ToDictionary(item => item.Key, item => ImageSource.FromBytes(item.Value), StringComparer.Ordinal);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Prediction artwork unavailable: {0}", error); }

        return result;
    }
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is not SeedSavedContainerPrediction other) return;
        _state = other._state; Container = other.Container; Region = other.Region;
        if (_prediction?.SetContext(ContextSnapshot, Current.World.Dimension, Container) == true) Images = new Dictionary<string, ImageSource>();
    }
    /// <inheritdoc/>
    protected override void OnDispose() { _disposed = true; _prediction?.Dispose(); base.OnDispose(); }
}