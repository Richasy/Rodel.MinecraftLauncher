// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Graphics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Servers;

/// <summary>Shares bounded SLP queries and decoded optional icons between visible server surfaces.</summary>
internal sealed class ServerStatusMonitor(LauncherClient client) : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _requests = new(4);
    private readonly Dictionary<string, string[]> _owners = [with(StringComparer.Ordinal)];
    private readonly List<Task> _batches = [];
    private Dictionary<string, ServerStatus> _values = [with(StringComparer.Ordinal)];
    private string[] _addresses = [];
    private CancellationTokenSource? _batch;
    private Task _watch = Task.CompletedTask;
    private ImageLoadServices? _images;
    private bool _started;
    private bool _disposed;
    internal IReadOnlyDictionary<string, ServerStatus> Values => _values;
    internal event Action? Changed;

    internal void Start(ImageLoadServices? images)
    {
        _images = images;
        if (_started || _disposed)
            return;
        _started = true;
        _watch = ObserveAsync();
        Refresh();
    }

    internal void SetTargets(
        string owner,
        IReadOnlyList<ServerFavorite> favorites,
        bool active,
        bool force = false)
    {
        if (_disposed)
            return;
        _owners[owner] = active ? [.. favorites.Select(item => item.Address).Distinct(StringComparer.Ordinal)] : [];
        var addresses = _owners.Values.SelectMany(items => items).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (!_addresses.SequenceEqual(addresses))
        {
            _batch?.Cancel();
            _batch = null;
            _addresses = addresses;
            if (_values.Count > Math.Max(64, addresses.Length))
                _values = _values.Where(pair => addresses.Contains(pair.Key, StringComparer.Ordinal)).ToDictionary(StringComparer.Ordinal);
        }

        Refresh(force);
    }

    private void Refresh(bool force = false)
    {
        if (!_started || _disposed || _batch is not null || _addresses.Length == 0)
            return;
        var now = DateTimeOffset.UtcNow;
        var due = _addresses.Where(address => force || !_values.TryGetValue(address, out var value) || value.Loading || value.CheckedAt is null || now - value.CheckedAt >= RefreshInterval).ToArray();
        if (due.Length == 0)
            return;
        var batch = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _batch = batch;
        foreach (var address in due)
        {
            if (_values.GetValueOrDefault(address)?.CheckedAt is null)
                Set(address, (_values.GetValueOrDefault(address) ?? new()) with { Loading = true });
        }

        _batches.RemoveAll(task => task.IsCompleted);
        _batches.Add(ReadBatchAsync(due, batch));
    }

    private async Task ReadBatchAsync(string[] addresses, CancellationTokenSource batch)
    {
        var token = batch.Token;
        try
        {
            await Task.WhenAll(addresses.Select(address => ReadAsync(address, batch)));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_batch, batch))
                _batch = null;
            batch.Dispose();
        }
    }

    private async Task ReadAsync(string address, CancellationTokenSource batch)
    {
        var token = batch.Token;
        await _requests.WaitAsync(token);
        try
        {
            var previous = _values.GetValueOrDefault(address);
            var value = await Task.Run(() => client.Servers.StatusAsync(address, ct: token), token);
            var icon = previous?.Icon;
            string? iconWarning = null;
            if (value.Online)
            {
                if (value.Status?["favicon"] is { } favicon)
                {
                    try
                    {
                        var images = _images ?? throw new InvalidOperationException("Image decoding services are unavailable.");
                        var encoded = favicon is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : throw new InvalidDataException("Server favicon must be a PNG data URI.");
                        icon = await Task.Run(() =>
                            {
                                var bytes = ServerIconData.Parse(encoded);
                                var decoded = images.Decoder.Decode(ImageSource.FromBytes(bytes, 64, 64), new(64, 64));
                                if (decoded.Width != 64 || decoded.Height != 64)
                                    throw new InvalidDataException("Decoded server favicon dimensions are invalid.");
                                return ImageSource.FromPixels(decoded);
                            }, token);
                    }
                    catch (Exception exception) when (exception is FormatException or InvalidDataException or IOException or ArgumentException or InvalidOperationException or NotSupportedException or COMException or Sprout.Interop.HResultException)
                    {
                        iconWarning = exception.Message;
                    }
                }
                else
                {
                    icon = null;
                }
            }

            value.Status?.Remove("favicon");
            if (!_disposed && !token.IsCancellationRequested && ReferenceEquals(_batch, batch))
                Set(address, new(value, Icon: icon, IconWarning: iconWarning, CheckedAt: DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or System.Text.Json.JsonException or InvalidOperationException)
        {
            if (!_disposed && !token.IsCancellationRequested && ReferenceEquals(_batch, batch))
                Set(address, new(Error: exception.Message, Icon: _values.GetValueOrDefault(address)?.Icon, CheckedAt: DateTimeOffset.UtcNow));
        }
        finally
        {
            _requests.Release();
        }
    }

    private void Set(string address, ServerStatus status)
    {
        _values = new(_values, StringComparer.Ordinal)
        {
            [address] = status
        };
        Changed?.Invoke();
    }

    private async Task ObserveAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                await Task.Delay(RefreshInterval, _lifetime.Token);
                Refresh();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    /// <summary>Stops refreshes and releases owned query resources after pending reads finish.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetime.Cancel();
        _batch?.Cancel();
        _ = FinishAsync();
    }

    private async Task FinishAsync()
    {
        try
        {
            await Task.WhenAll(_batches.Append(_watch));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            _batch?.Dispose();
            _batch = null;
            _requests.Dispose();
            _lifetime.Dispose();
        }
    }
}