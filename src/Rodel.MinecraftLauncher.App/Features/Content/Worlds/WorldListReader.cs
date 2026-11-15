// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;

namespace Rodel.MinecraftLauncher.App.Features.Content;

/// <summary>Reads Core worlds and bounded optional thumbnails away from the UI thread.</summary>
internal static class WorldListReader
{
    internal static async Task<WorldListSnapshot> ReadAsync(LauncherClient client, WorldListRead request, CancellationToken cancellationToken)
    {
        var data = await client.Worlds.ListAsync(request.InstanceId, cancellationToken).ConfigureAwait(false);
        var previous = request.Previous.ToDictionary(item => item.Folder, StringComparer.Ordinal);
        var result = new List<WorldItem>();
        foreach (var item in data)
        {
            byte[]? icon = null;
            var warning = item.Warning;
            if (item.Icon is { } file)
            {
                try
                {
                    icon = await WorldIconReader.ReadAsync(file, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    warning = warning is null ? error.Message : warning + Environment.NewLine + error.Message;
                }
            }

            if (icon is not null && previous.GetValueOrDefault(item.Folder)?.Icon is { } priorIcon && icon.AsSpan().SequenceEqual(priorIcon))
                icon = priorIcon;
            result.Add(new(
                    request.InstanceId,
                    item.Folder,
                    item.Name,
                    item.Version ?? string.Empty,
                    item.GameType,
                    item.Hardcore == true,
                    item.LastPlayed,
                    warning,
                    icon));
        }

        var recovery = client.Worlds.ListRecovery(request.InstanceId);
        return new(request.InstanceId, result, recovery.Count(row => !row.HasError), string.Join(Environment.NewLine, recovery.Where(row => row.HasError).Select(row => row.ErrorText)));
    }
}