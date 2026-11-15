// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Collections.Concurrent;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Confines real Core storage and reactive continuations without launching an unpackaged application window.</summary>
internal sealed class StateTestScope : IDisposable
{
    internal StateTestScope()
    {
        Root = Path.Combine(Path.GetTempPath(), "RodelAppStateTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Client = new(new LauncherPaths(Root));
    }

    internal string Root { get; }
    internal LauncherClient Client { get; }

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(Root, recursive: true);

    internal static void Run(Func<Task> action)
    {
        var previous = SynchronizationContext.Current;
        using var context = new StateContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try { context.Run(action); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private sealed class StateContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
        /// <inheritdoc/>
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));
        internal void Run(Func<Task> action)
        {
            var task = action();
            _ = task.ContinueWith(_ => _queue.CompleteAdding(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable()) callback(state);
            task.GetAwaiter().GetResult();
        }
        /// <inheritdoc/>
        public void Dispose() => _queue.Dispose();
    }
}