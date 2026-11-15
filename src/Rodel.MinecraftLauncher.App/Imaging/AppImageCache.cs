// Copyright (c) Richasy. All rights reserved.

using Sprout.HttpImages;
using System.Globalization;

namespace Rodel.MinecraftLauncher.App.Imaging;

internal sealed class AppImageCache
{
    internal AppImageCache(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
        Loader = new(new HttpImageOptions
        {
            DiskCacheEnabled = true,
            DiskCacheRoot = DirectoryPath,
            MemoryBudgetBytes = 16L * 1024 * 1024,
            HttpClientFactory = static () => new HttpClient(new ImageHttpHandler()) { Timeout = TimeSpan.FromSeconds(8), },
            RequestModifier = static request => request.Headers.UserAgent.ParseAdd("Rodel.MinecraftLauncher"),
        });
    }

    internal string DirectoryPath { get; }
    internal HttpImageLoader Loader { get; }

    internal Task<long> GetSizeAsync() => Task.Run(() =>
        {
            try
            {
                var files = new DirectoryInfo(DirectoryPath).EnumerateFiles("*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = false,
                    AttributesToSkip = FileAttributes.ReparsePoint
                });
                long bytes = 0;
                foreach (var file in files)
                {
                    try
                    {
                        bytes = checked(bytes + file.Length);
                    }
                    catch (FileNotFoundException)
                    {
                    }
                }

                return bytes;
            }
            catch (DirectoryNotFoundException)
            {
                return 0L;
            }
        });

    internal async Task<long> ClearAsync()
    {
        Loader.ClearMemoryCache();
        Loader.ClearFailureCache();
        await Loader.ClearDiskCacheAsync().ConfigureAwait(false);
        Loader.ClearMemoryCache();
        return await GetSizeAsync().ConfigureAwait(false);
    }

    internal static string FormatSize(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return string.Create(CultureInfo.CurrentCulture, $"{value:0.#} {units[index]}");
    }
}