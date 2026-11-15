// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Windows.Wdk.Storage.FileSystem;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;

namespace Rodel.MinecraftLauncher.Core.Multiplayer;

/// <summary>Keeps unmodified component images at stable per-user paths without changing firewall policy.</summary>
internal static class TerracottaRuntime
{
    internal static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Rodel.MinecraftLauncher", "Components", "Terracotta", "0.4.2-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());

    internal static async Task<string> PrepareAsync(string shippedDirectory, CancellationToken ct)
    {
        using var lease = await ResourceLease.AcquireAsync(DirectoryPath, ct).ConfigureAwait(false);
        foreach (var name in new[] { "terracotta.exe", "VCRUNTIME140.DLL", "NOTICE.txt", "LICENSE.txt" })
            await CopyVerifiedAsync(Path.Combine(shippedDirectory, name), Path.Combine(DirectoryPath, name), ct).ConfigureAwait(false);
        return Path.Combine(DirectoryPath, "terracotta.exe");
    }

    internal static async Task RedirectEngineAsync(string temporaryRoot, int worker, DateTimeOffset started, CancellationToken ct)
    {
        // This private layout is pinned to the audited, hash-verified upstream 0.4.2 binary.
        var work = System.IO.Directory.EnumerateDirectories(Path.Combine(temporaryRoot, "terracotta"), "*-" + worker)
            .Where(path => System.IO.Directory.GetCreationTimeUtc(path) >= started.UtcDateTime.AddSeconds(-5))
            .OrderByDescending(System.IO.Directory.GetCreationTimeUtc).FirstOrDefault()
            ?? throw new IOException("The owned component working directory is unavailable.");
        var source = Path.Combine(work, "embedded-easytier");
        var target = Path.Combine(DirectoryPath, "engine");
        if (System.IO.Directory.Exists(source) && (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
        {
            if (string.Equals(System.IO.Directory.ResolveLinkTarget(source, true)?.FullName, target, StringComparison.OrdinalIgnoreCase)) return;
            throw new IOException("The component engine directory points outside its managed runtime.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        // The pinned Windows x64 and ARM64 payloads both contain these three complete files.
        // Exclusive opens wait for upstream extraction to finish; no engine is started before this step.
        var files = new[] { "easytier-core.exe", "easytier-cli.exe", "Packet.dll" };
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            var opened = new List<FileStream>();
            try
            {
                foreach (var name in files) opened.Add(new(Path.Combine(source, name), FileMode.Open, FileAccess.Read, FileShare.None, 65536, FileOptions.Asynchronous));
                using var lease = await ResourceLease.AcquireAsync(target, deadline.Token).ConfigureAwait(false);
                System.IO.Directory.CreateDirectory(target);
                for (var index = 0; index < files.Length; index++)
                    await PublishAsync(opened[index], Path.Combine(target, files[index]), deadline.Token).ConfigureAwait(false);
                break;
            }
            catch (IOException) when (!deadline.IsCancellationRequested) { }
            finally { foreach (var file in opened) await file.DisposeAsync().ConfigureAwait(false); }

            await Task.Delay(100, deadline.Token).ConfigureAwait(false);
        }

        var backup = source + ".original";
        System.IO.Directory.Move(source, backup);
        try { CreateJunction(source, target); }
        catch
        {
            if (System.IO.Directory.Exists(source)) System.IO.Directory.Delete(source);
            System.IO.Directory.Move(backup, source);
            throw;
        }
        // Only the three verified files copied above belong to this exact worker's original directory.
        foreach (var name in files) File.Delete(Path.Combine(backup, name));
        System.IO.Directory.Delete(backup);
    }

    internal static async Task CopyVerifiedAsync(string source, string destination, CancellationToken ct)
    {
        var input = File.OpenRead(source);
        await using (input.ConfigureAwait(false)) await PublishAsync(input, destination, ct).ConfigureAwait(false);
    }

    private static async Task PublishAsync(FileStream source, string target, CancellationToken ct)
    {
        var expected = await SHA256.HashDataAsync(source, ct).ConfigureAwait(false);
        source.Position = 0;
        if (File.Exists(target))
        {
            var current = File.OpenRead(target);
            await using var disposal = current.ConfigureAwait(false);
            if ((await SHA256.HashDataAsync(current, ct).ConfigureAwait(false)).AsSpan().SequenceEqual(expected)) return;
        }

        await AtomicFile.CopyAsync(source, target, 65536, FileOptions.Asynchronous, ct).ConfigureAwait(false);
    }

    internal static unsafe void CreateJunction(string path, string target)
    {
        var full = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
        var substitute = @"\??\" + full;
        var text = substitute + '\0' + full + '\0';
        var size = sizeof(REPARSE_DATA_BUFFER) + (text.Length * sizeof(char));
        var storage = new byte[size];
        fixed (byte* bytes = storage)
        {
            var data = (REPARSE_DATA_BUFFER*)bytes;
            data->ReparseTag = PInvoke.IO_REPARSE_TAG_MOUNT_POINT;
            ref var mount = ref data->Anonymous.MountPointReparseBuffer;
            mount.SubstituteNameLength = checked((ushort)(substitute.Length * sizeof(char)));
            mount.PrintNameOffset = checked((ushort)((substitute.Length + 1) * sizeof(char)));
            mount.PrintNameLength = checked((ushort)(full.Length * sizeof(char)));
            text.AsSpan().CopyTo(mount.PathBuffer.AsSpan(text.Length));
            var header = checked((int)((byte*)&data->Anonymous.MountPointReparseBuffer.PathBuffer - bytes));
            var length = header + (text.Length * sizeof(char));
            data->ReparseDataLength = checked((ushort)(length - Marshal.OffsetOf<REPARSE_DATA_BUFFER>(nameof(REPARSE_DATA_BUFFER.Anonymous)).ToInt32()));
            System.IO.Directory.CreateDirectory(path);
            using var handle = PInvoke.CreateFile(path, (uint)GENERIC_ACCESS_RIGHTS.GENERIC_WRITE, FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE,
                null, FILE_CREATION_DISPOSITION.OPEN_EXISTING, FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS, null);
            if (handle.IsInvalid || !PInvoke.DeviceIoControl(handle, PInvoke.FSCTL_SET_REPARSE_POINT, storage.AsSpan(0, length), [], out _))
                throw new IOException("Could not create the component's stable engine directory.", new Win32Exception(Marshal.GetLastPInvokeError()));
        }
    }
}