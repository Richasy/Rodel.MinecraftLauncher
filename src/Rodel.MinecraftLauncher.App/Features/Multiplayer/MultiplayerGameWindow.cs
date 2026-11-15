// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using System.Diagnostics;
using System.Globalization;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Rodel.MinecraftLauncher.App.Features.Multiplayer;

/// <summary>Restores only the game process already bound to this room, guarding against PID reuse.</summary>
internal static class MultiplayerGameWindow
{
    internal static bool Restore(LauncherClient client, string? sessionId)
    {
        if (sessionId is null) return false;
        try
        {
            var session = client.Sessions.Get(sessionId);
            if (session.Status != "running" || session.ProcessId is not { } pid
                || !DateTimeOffset.TryParse(session.ProcessStartUtc, CultureInfo.InvariantCulture, DateTimeStyles.None, out var started))
            {
                return false;
            }

            using var process = Process.GetProcessById(pid);
            if (process.HasExited || process.StartTime.ToUniversalTime() != started.UtcDateTime || process.MainWindowHandle == 0) return false;
            var hwnd = (HWND)process.MainWindowHandle;
            PInvoke.ShowWindowAsync(hwnd, SHOW_WINDOW_CMD.SW_RESTORE);
            return PInvoke.SetForegroundWindow(hwnd) || PInvoke.GetForegroundWindow() == hwnd;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }
}