// Copyright (c) Richasy. All rights reserved.

using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32.System.JobObjects;
using PInvoke = Windows.Win32.AssistantPInvoke;

namespace Rodel.MinecraftLauncher.Assistant;

/// <summary>Owns only MCP children started by this host, including descendants surviving their parent.</summary>
internal sealed class AiChildProcessJob : IDisposable
{
    private readonly SafeFileHandle _handle;

    /// <summary>Creates a kill-on-close Windows job, optionally permitting launcher-owned children to break away.</summary>
    public AiChildProcessJob(bool independentLauncherChildren = false)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Managed MCP process ownership requires Windows.");
        _handle = PInvoke.CreateJobObject(null, null);
        if (_handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            _handle.Dispose();
            throw new Win32Exception(error);
        }

        var flags = JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (independentLauncherChildren) flags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK;
        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION { BasicLimitInformation = new() { LimitFlags = flags } };
        if (!PInvoke.SetInformationJobObject(_handle, JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
            MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in limits, 1))))
        {
            var error = Marshal.GetLastPInvokeError();
            _handle.Dispose();
            throw new Win32Exception(error);
        }
    }

    /// <summary>Assigns a newly started MCP child to this job before protocol initialization.</summary>
    public void Assign(Process process)
    {
        if (!PInvoke.AssignProcessToJobObject(_handle, process.SafeHandle))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
    /// <summary>Closes the job and terminates its remaining owned processes.</summary>
    public void Dispose() => _handle.Dispose();

}