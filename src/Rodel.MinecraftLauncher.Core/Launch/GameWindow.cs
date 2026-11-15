// Copyright (c) Richasy. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;

namespace Rodel.MinecraftLauncher.Core.Launch;

internal static class GameWindow
{
    /// <summary>Requests system-menu close on a visible window owned by this process or a verified descendant.</summary>
    public static bool RequestClose(Process process, out int? windowProcessId) =>
        FindWindow(process, true, out windowProcessId);

    internal static bool IsVisible(Process process, out string? warning)
    {
        string? failure = null;
        var visible = FindWindow(process, false, out _, error => failure = error.Message);
        warning = failure;
        return visible;
    }

    private static bool FindWindow(Process process, bool close, out int? windowProcessId, Action<Exception>? reportError = null)
    {
        windowProcessId = null;
        if (TryWindow(process, [process], close))
        {
            windowProcessId = process.Id;
            return true;
        }

        // Oracle javapath and user-provided Java wrappers can stay alive while the
        // actual JVM owns the window. Follow kernel parent IDs, never executable names.
        var snapshotStarted = DateTime.UtcNow;
        var parents = SnapshotParents(reportError);
        var owned = new List<Process>();
        var pending = new Queue<(Process Process, DateTime Started, Process[] Chain)>();
        var visited = new HashSet<int>
        {
            process.Id
        };
        try
        {
            if (process.HasExited)
                return false;
            pending.Enqueue((process, process.StartTime.ToUniversalTime(), [process]));
            while (pending.TryDequeue(out var parent))
            {
                if (parent.Process.HasExited)
                    continue;
                foreach (var childId in parents.Where(x => x.Value == parent.Process.Id).Select(x => x.Key))
                {
                    if (!visited.Add(childId))
                        continue;
                    Process? child = null;
                    try
                    {
                        child = Process.GetProcessById(childId);
                        var started = child.StartTime.ToUniversalTime();
                        // A surviving child whose old parent's PID was reused predates
                        // the new parent. Do not attach that unrelated process to this tree.
                        // Also reject a PID reused after the parent-ID snapshot was taken.
                        if (child.HasExited || started < parent.Started || started > snapshotStarted)
                        {
                            child.Dispose();
                            continue;
                        }

                        owned.Add(child);
                        var chain = parent.Chain.Append(child).ToArray();
                        if (TryWindow(child, chain, close))
                        {
                            windowProcessId = child.Id;
                            return true;
                        }

                        pending.Enqueue((child, started, chain));
                    }
                    catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        if (error is System.ComponentModel.Win32Exception) reportError?.Invoke(error);
                        if (child is not null && !owned.Contains(child))
                            child.Dispose();
                    }
                }
            }

            return false;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            if (error is System.ComponentModel.Win32Exception) reportError?.Invoke(error);
            return false;
        }
        finally
        {
            foreach (var child in owned)
                child.Dispose();
        }
    }

    private static bool TryWindow(Process process, IReadOnlyList<Process> chain, bool close)
    {
        if (chain.Any(item => item.HasExited))
            return false;
        process.Refresh();
        var window = process.MainWindowHandle;
        if (window == 0)
            return false;
        _ = PInvoke.GetWindowThreadProcessId(new(window), out var owner);
        if (owner != process.Id || chain.Any(item => item.HasExited))
            return false;
        // The system-menu close action also reaches LWJGL 2's close_requested flag.
        // Direct WM_CLOSE (Process.CloseMainWindow) destroys its window without
        // notifying the Minecraft loop on 1.7.10/1.12.2.
        return !close || PInvoke.PostMessage(new(window), PInvoke.WM_SYSCOMMAND, new(PInvoke.SC_CLOSE), default);
    }

    private static Dictionary<int, int> SnapshotParents(Action<Exception>? reportError)
    {
        var result = new Dictionary<int, int>();
        using var snapshot = PInvoke.CreateToolhelp32Snapshot_SafeHandle(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);
        if (snapshot.IsInvalid)
        {
            reportError?.Invoke(new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
            return result;
        }

        var entry = new PROCESSENTRY32W
        {
            dwSize = (uint)Unsafe.SizeOf<PROCESSENTRY32W>()
        };
        if (!PInvoke.Process32FirstW(snapshot, ref entry))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != (int)WIN32_ERROR.ERROR_NO_MORE_FILES) reportError?.Invoke(new System.ComponentModel.Win32Exception(error));
            return result;
        }

        do
        {
            result[unchecked((int)entry.th32ProcessID)] = unchecked((int)entry.th32ParentProcessID);
        }
        while (PInvoke.Process32NextW(snapshot, ref entry));
        return result;
    }
}