// Copyright (c) Richasy. All rights reserved.

using Microsoft.Windows.Storage;
using Sprout;

namespace Rodel.MinecraftLauncher.App.Settings;

/// <summary>Resolves app-owned files and preferences for package and current-user EXE deployments.</summary>
internal static class AppStorage
{
    internal static string LocalPath => PackageIdentity.IsPackaged
        ? ApplicationData.GetDefault().LocalPath
        : Path.Combine(UnpackagedRoot, "LocalState");

    internal static string CachePath => PackageIdentity.IsPackaged
        ? ApplicationData.GetDefault().LocalCachePath
        : Path.Combine(UnpackagedRoot, "LocalCache");

    internal static string DataDirectory => Directory.GetParent(LocalPath)!.FullName;

    private static string UnpackagedRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Richasy", "RMCL");

    internal static IDictionary<string, object> OpenSettings(string container) => PackageIdentity.IsPackaged
        ? ApplicationData.GetDefault().LocalSettings.CreateContainer(container, ApplicationDataCreateDisposition.Always).Values
        : new RegistrySettings(@"Software\Richasy\RMCL\" + container);
}