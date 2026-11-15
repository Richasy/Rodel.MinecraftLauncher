// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Settings;
using Sprout;

namespace Rodel.MinecraftLauncher.App.Features.Shell;

internal static class WindowPreferences
{
    internal static WindowPlacement? Read(AppSettings settings) => WindowPlacement.TryParse(settings.WindowPlacement, out var placement) ? placement : null;

    internal static void Attach(Window window, AppSettings settings)
    {
        window.Closing += (_, _) =>
        {
            try
            {
                settings.WindowPlacement = window.SavePlacement().Serialize();
            }
            catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.TraceError("Window preferences could not be saved: {0}", exception.Message);
            }
        };
    }
}