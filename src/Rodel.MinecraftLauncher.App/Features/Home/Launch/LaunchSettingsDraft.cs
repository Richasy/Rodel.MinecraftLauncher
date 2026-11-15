// Copyright (c) Richasy. All rights reserved.

using System.Globalization;

namespace Rodel.MinecraftLauncher.App.Features.Home;

/// <summary>Distinguishes an inherited bridge preference from either explicit instance override.</summary>
internal enum GameBridgeSettingMode
{
    Inherit,
    Disabled,
    Enabled
}

internal sealed record LaunchSettingsBaseline(
    string Java,
    string Memory,
    string Player,
    GameBridgeSettingMode GameBridge = GameBridgeSettingMode.Inherit,
    bool AutomaticMemory = false);

internal sealed record LaunchSettingsDraft(
    string InstanceId,
    string Name,
    string Version,
    string Java,
    string Memory,
    string Player,
    string Error = "",
    bool Saving = false,
    LaunchSettingsBaseline? Baseline = null,
    long? PhysicalMemoryMb = null,
    long? AvailableMemoryMb = null,
    bool MemoryEditing = false,
    double MemoryEditValue = double.NaN,
    bool VersionUnavailable = false,
    string? RepairVersion = null,
    bool RepairVersionSelected = false,
    GameBridgeSettingMode GameBridge = GameBridgeSettingMode.Inherit,
    bool GlobalGameBridge = false,
    bool AutomaticMemory = false,
    bool PlayerEditing = false)
{
    internal double MemoryValue => double.TryParse(Memory, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : double.NaN;
    internal double MemoryEditorValue => MemoryEditing ? MemoryEditValue : MemoryValue;
    internal bool JavaChanged => Baseline is { } original && !string.Equals(Java.Trim(), original.Java.Trim(), StringComparison.OrdinalIgnoreCase);
    internal bool MemoryChanged => !AutomaticMemory && Baseline is { } original && !SameMemory(Memory, original.Memory);
    internal bool MemoryModeChanged => Baseline is { } original && AutomaticMemory != original.AutomaticMemory;
    internal bool PlayerChanged => Baseline is { } original && !string.Equals(Player.Trim(), original.Player.Trim(), StringComparison.Ordinal);
    internal bool GameBridgeChanged => Baseline is { } original && GameBridge != original.GameBridge;
    internal bool HasChanges => Baseline is not null && (RepairVersionSelected || JavaChanged || MemoryChanged || MemoryModeChanged || PlayerChanged || GameBridgeChanged);
    private string MemoryValidationError => MemoryChanged && (!double.IsFinite(MemoryValue) || MemoryValue != Math.Truncate(MemoryValue) || MemoryValue > int.MaxValue)
        ? Resources.LaunchMemoryIntegerError
        : MemoryChanged && MemoryValue < 1
        ? string.Format(Resources.LaunchMemoryMinimumError, 1) : string.Empty;
    internal string ValidationError => MemoryValidationError.Length > 0
        ? MemoryValidationError
        : PlayerChanged && string.IsNullOrWhiteSpace(Player) ? Resources.LaunchPlayerRequired : string.Empty;
    internal string MemoryWarning => AutomaticMemory || ValidationError.Length > 0
        ? string.Empty
        : MemoryValue < 2048
        ? Resources.LaunchMemoryLowWarning
        : (AvailableMemoryMb is { } available && MemoryValue > Math.Max(0, available - 1024)) || (PhysicalMemoryMb is { } total && MemoryValue > total * .75) ? Resources.LaunchMemoryHighWarning : string.Empty;
    internal string MemoryPolicy => PhysicalMemoryMb is { } total ? string.Format(Resources.LaunchMemoryPolicy, 1, total, AvailableMemoryMb?.ToString(CultureInfo.CurrentCulture) ?? Resources.ValueNotAvailable) : string.Format(Resources.LaunchMemoryPolicyUnknown, 1);
    internal bool CanSave => !Saving && HasChanges && ValidationError.Length == 0;
    internal LaunchSettingsDraft AutoSaveChanges => Baseline is { } original
        ? this with
        {
            Saving = false,
            Memory = MemoryEditing || MemoryValidationError.Length > 0 ? original.Memory : Memory,
            Player = PlayerEditing || string.IsNullOrWhiteSpace(Player) ? original.Player : Player
        }
        : this;

    internal LaunchSettingsDraft AcceptSaved(LaunchSettingsDraft saved) => this with
    {
        Baseline = new(
            saved.Java,
            saved.MemoryChanged ? saved.Memory : saved.Baseline!.Memory,
            saved.Player,
            saved.GameBridge,
            saved.AutomaticMemory),
        RepairVersionSelected = RepairVersionSelected && !saved.RepairVersionSelected,
        VersionUnavailable = VersionUnavailable && !saved.RepairVersionSelected,
        Error = string.Empty
    };

    private static bool SameMemory(string current, string original) => string.Equals(current, original, StringComparison.Ordinal) || (double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.TryParse(original, NumberStyles.Float, CultureInfo.InvariantCulture, out var baseline) && value.Equals(baseline));
}