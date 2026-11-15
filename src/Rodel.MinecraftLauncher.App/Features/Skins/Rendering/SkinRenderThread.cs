// Copyright (c) Richasy. All rights reserved.

using Sprout.Interop;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Rodel.MinecraftLauncher.App.Features.Skins.Rendering;

/// <summary>Owns the skin views' D3D context on one thread; each live view leases the same device.</summary>
internal static class SkinRenderThread
{
    private static readonly BlockingCollection<Action> Work = [];
    private static DeviceLease? _current;

    static SkinRenderThread() => new Thread(() =>
        {
            while (true)
                RunNext();
        })
    {
        IsBackground = true,
        Name = "Skin D3D11"
    }.Start();

    internal static void Post(Action action) => Work.Add(action);

    // A consuming iterator retains its last yielded delegate while idle, which can retain a closed editor.
    // End the work item's stack lifetime before the next blocking take.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunNext()
    {
        var action = Work.Take();
        try { action(); }
        catch (Exception error) { ReportFailure(error); }
    }

    internal static void ReportFailure(Exception error)
    {
        try { Trace.TraceError("Skin render work failed: {0}", error); }
        catch (Exception)
        {
            // A diagnostic sink cannot terminate the device owner or strand the remaining views' cleanup.
        }
    }

    // Only the render thread acquires, retires and releases device ownership.
    internal static DeviceLease Acquire()
    {
        _current ??= new(GraphicsDevice.Create());
        _current.References++;
        return _current;
    }

    internal sealed class DeviceLease(GraphicsDevice device)
    {
        internal GraphicsDevice Device { get; } = device;
        internal int References { get; set; }

        internal void Retire()
        {
            if (ReferenceEquals(_current, this))
                _current = null;
        }

        internal void Release()
        {
            if (--References == 0)
            {
                Retire();
                Device.Dispose();
            }
        }
    }
}