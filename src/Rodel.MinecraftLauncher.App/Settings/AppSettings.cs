// Copyright (c) Richasy. All rights reserved.

using Microsoft.Windows.Storage;
using Sprout;
using Sprout.Theming;

namespace Rodel.MinecraftLauncher.App.Settings;

/// <summary>Application preferences, separate from Core's game and instance configuration.</summary>
internal sealed partial class AppSettings : SettingsBase, IDisposable
{
    internal const string UninitializedCapePreviewSkin = "-";
    private readonly ApplicationData? _data;

    internal AppSettings(IDictionary<string, object> values) : base(values)
    {
    }

    private AppSettings(ApplicationData data, string container) : base(data.LocalSettings.CreateContainer(container, ApplicationDataCreateDisposition.Always).Values) => _data = data;

    internal static AppSettings Open(string container = "Preferences") => PackageIdentity.IsPackaged
        ? new(ApplicationData.GetDefault(), container)
        : new(AppStorage.OpenSettings(container));
    /// <summary>Gets or commits the exact release version dismissed by the user; later versions remain discoverable.</summary>
    [SettingProperty("")]
    public partial string SkippedUpdateVersion { get; set; }
    /// <summary>Sprout's versioned normal bounds, physical position, DPI and maximized restore target.</summary>
    [SettingProperty("")]
    public partial string WindowPlacement { get; set; }
    [SettingProperty("")]
    public partial string Language { get; set; }
    /// <summary>Gets or commits the app's requested theme; the system's high-contrast mode still takes precedence.</summary>
    [SettingProperty(ThemeRequest.FollowSystem)]
    public partial ThemeRequest ThemePreference { get; set; }
    [SettingProperty("Tray")]
    public partial string AfterLaunchBehavior { get; set; }
    /// <summary>Gets or commits whether the app exposes multiplayer navigation and room actions; Core and CLI capabilities are independent.</summary>
    [SettingProperty(false)]
    public partial bool MultiplayerEnabled { get; set; }
    /// <summary>Gets or commits whether an active local host may show its room status while the main window is hidden or minimized.</summary>
    [SettingProperty(true)]
    public partial bool MultiplayerMiniWindow { get; set; }
    [SettingProperty("Exit")]
    public partial string CloseBehavior { get; set; }
    [SettingProperty("")]
    public partial string DataDirectory { get; set; }
    [SettingProperty("")]
    public partial string SelectedInstanceId { get; set; }
    [SettingProperty("local")]
    public partial string HomeTargetKind { get; set; }
    [SettingProperty("")]
    public partial string SelectedServerId { get; set; }
    [SettingProperty("")]
    public partial string LightWallpaper { get; set; }
    [SettingProperty("")]
    public partial string DarkWallpaper { get; set; }
    /// <summary>Custom skin palettes and selected stable identity, committed together without changing skin documents.</summary>
    [SettingProperty("")]
    public partial string SkinPalettes { get; set; }
    /// <summary>Gets or commits the editor's opt-out from the limb-copy overwrite warning, independently of skin projects.</summary>
    [SettingProperty(false)]
    public partial bool SkipSkinCopyOppositeWarning { get; set; }
    /// <summary>Gets or commits the character used by all cape previews. An empty value is an explicit clear.</summary>
    [SettingProperty(UninitializedCapePreviewSkin)]
    public partial string CapePreviewSkin { get; set; }

    public void Dispose() => _data?.Dispose();
}