// Copyright (c) Richasy. All rights reserved.

using System.Collections;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.JobObjects;
using Windows.Win32.System.Threading;

namespace Rodel.MinecraftLauncher.Core.Multiplayer;

/// <summary>Owns only this room's network process tree; Windows revokes it when the owning process dies.</summary>
internal sealed class NetworkProcessJob : IDisposable
{
    private readonly SafeHandle _job;
    private NetworkProcessJob(SafeHandle job) => _job = job;

    internal static unsafe NetworkProcessJob Start(string executable, string rendezvous, string temporary)
    {
        var job = PInvoke.CreateJobObject(null, null);
        if (job.IsInvalid) throw Failure();
        var owner = new NetworkProcessJob(job);
        nint allocation = 0;
        LPPROC_THREAD_ATTRIBUTE_LIST attributes = default;
        var initialized = false;
        try
        {
            var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            if (!PInvoke.SetInformationJobObject(job, JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation, new ReadOnlySpan<byte>(&limits, sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)))) throw Failure();
            nuint size = 0;
            _ = PInvoke.InitializeProcThreadAttributeList(default, 1, 0, &size);
            allocation = Marshal.AllocHGlobal(checked((nint)size));
            attributes = new((void*)allocation);
            if (!PInvoke.InitializeProcThreadAttributeList(attributes, 1, 0, &size)) throw Failure();
            initialized = true;
            var handle = new HANDLE((void*)job.DangerousGetHandle());
            if (!PInvoke.UpdateProcThreadAttribute(attributes, 0, PInvoke.PROC_THREAD_ATTRIBUTE_JOB_LIST, &handle, (nuint)sizeof(HANDLE), null, null)) throw Failure();
            var startup = new STARTUPINFOEXW { StartupInfo = new() { cb = (uint)sizeof(STARTUPINFOEXW) }, lpAttributeList = attributes };
            // These are executable/file paths, not shell text; Windows paths cannot contain quotes.
            var command = ($"\"{executable}\" --hmcl \"{rendezvous}\"\0").ToCharArray();
            var environment = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>().ToDictionary(item => (string)item.Key, item => (string?)item.Value, StringComparer.OrdinalIgnoreCase);
            environment["TEMP"] = temporary; environment["TMP"] = temporary;
            var block = (string.Join('\0', environment.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select(item => item.Key + "=" + item.Value)) + "\0\0").ToCharArray();
            fixed (char* commandPointer = command)
            fixed (char* environmentPointer = block)
            fixed (char* applicationPointer = executable)
            fixed (char* directoryPointer = Path.GetDirectoryName(executable))
            {
                PROCESS_INFORMATION process;
                if (!PInvoke.CreateProcess(new PCWSTR(applicationPointer), new PWSTR(commandPointer), null, null, false,
                    PROCESS_CREATION_FLAGS.CREATE_NO_WINDOW | PROCESS_CREATION_FLAGS.CREATE_UNICODE_ENVIRONMENT | PROCESS_CREATION_FLAGS.EXTENDED_STARTUPINFO_PRESENT,
                    environmentPointer, new PCWSTR(directoryPointer), (STARTUPINFOW*)&startup, &process))
                {
                    throw Failure();
                }
                // The job is assigned atomically by CreateProcess before any bootstrap/fork can run.
                _ = PInvoke.CloseHandle(process.hThread);
                _ = PInvoke.CloseHandle(process.hProcess);
            }

            return owner;
        }
        catch { owner.Dispose(); throw; }
        finally
        {
            if (initialized) PInvoke.DeleteProcThreadAttributeList(attributes);
            if (allocation != 0) Marshal.FreeHGlobal(allocation);
        }
    }

    internal bool Contains(System.Diagnostics.Process process)
    {
        if (!PInvoke.IsProcessInJob(process.SafeHandle, _job, out var member)) throw Failure();
        return member;
    }

    private static IOException Failure() => new("Windows could not bind the multiplayer process lifetime.", new Win32Exception(Marshal.GetLastPInvokeError()));

    /// <summary>Closes the non-inherited job handle, terminating only its assigned network tree.</summary>
    public void Dispose() => _job.Dispose();
}