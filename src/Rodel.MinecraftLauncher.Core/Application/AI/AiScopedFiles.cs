// Copyright (c) Richasy. All rights reserved.

using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Windows handle-based reads that keep every directory identity pinned until the file closes.</summary>
public static class AiScopedFiles
{
    /// <summary>Pins an existing directory and every ancestor against replacement until the returned scope is disposed.</summary>
    public static DirectoryScope OpenDirectory(string root, string relativePath = "") => OpenDirectoryCore(root, relativePath, false);

    /// <summary>Creates missing descendants one component at a time while pinning and checking each directory; call only after journaling an authorized mutation.</summary>
    public static DirectoryScope EnsureDirectory(string root, string relativePath) => OpenDirectoryCore(root, relativePath, true);

    private static DirectoryScope OpenDirectoryCore(string root, string relativePath, bool create)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Scoped AI file access currently requires Windows.");
        if (!Path.IsPathFullyQualified(root) || root.StartsWith(@"\\", StringComparison.Ordinal) || Path.IsPathRooted(relativePath)
            || relativePath.Contains(':') || relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "." or ".."))
        {
            throw new ArgumentException("Use a real local directory and a relative path without traversal or alternate streams.");
        }

        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(root, relativePath)));
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!target.Equals(root, StringComparison.OrdinalIgnoreCase) && !target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new LauncherOperationException("AI_FILE_OUTSIDE_SCOPE", "The directory is outside its selected root.");
        var handles = new List<SafeFileHandle>();
        try
        {
            var volume = Path.GetPathRoot(target)!;
            var current = volume;
            handles.Add(OpenHandle(current, directory: true));
            foreach (var part in Path.GetRelativePath(volume, target).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == ".") continue;
                current = Path.Combine(current, part);
                if (create && AiPathAccess.Contains(root, current) && !Directory.Exists(current)) Directory.CreateDirectory(current);
                handles.Add(OpenHandle(current, directory: true));
            }

            if (!FinalPath(handles[^1]).Equals(target, StringComparison.OrdinalIgnoreCase))
                throw new LauncherOperationException("AI_FILE_IDENTITY_CHANGED", "The directory's final identity changed.");
            return new DirectoryScope(target, handles);
        }
        catch
        {
            foreach (var handle in handles) handle.Dispose();
            throw;
        }
    }

    /// <summary>Reads bounded UTF-8/UTF-16 text while pinning the selected file and its ancestors against replacement.</summary>
    public static string ReadText(string root, string relativePath, int maximumBytes = 128000)
    {
        using var scope = Open(root, relativePath);
        if (scope.Stream.Length > maximumBytes)
            throw new LauncherOperationException("AI_FILE_TOO_LARGE", "This text file exceeds its bounded read budget. Select a smaller file or range.");
        var bytes = new byte[checked((int)scope.Stream.Length)];
        scope.Stream.ReadExactly(bytes);
        try
        {
            var encoding = bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }) ? new UnicodeEncoding(false, true, true)
                : bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }) ? new UnicodeEncoding(true, true, true)
                : (Encoding)new UTF8Encoding(false, true);
            var preamble = encoding.GetPreamble();
            var offset = bytes.AsSpan().StartsWith(preamble) ? preamble.Length : 0;
            var text = encoding.GetString(bytes, offset, bytes.Length - offset);
            if (text.Contains('\0')) throw new DecoderFallbackException();
            return text.TrimStart('\ufeff');
        }
        catch (DecoderFallbackException)
        {
            throw new LauncherOperationException("AI_FILE_ENCODING_UNSUPPORTED", "The file is not supported UTF-8/UTF-16 text. No replacement-decoded or binary content was returned.");
        }
    }

    /// <summary>Opens a real local file without following reparse points and retains every checked directory handle.</summary>
    public static Scope Open(string root, string relativePath)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Scoped AI file access currently requires Windows.");
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("Select an absolute scoped directory.");
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Path.IsPathFullyQualified(root) || root.StartsWith(@"\\", StringComparison.Ordinal) || Path.IsPathRooted(relativePath)
            || relativePath.Contains(':') || relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "." or ".."))
        {
            throw new ArgumentException("Use a local absolute root and a root-relative path without traversal, device paths or alternate data streams.");
        }

        var target = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new LauncherOperationException("AI_FILE_OUTSIDE_SCOPE", "The requested file is outside its selected directory.");
        var handles = new List<SafeFileHandle>();
        try
        {
            var volume = Path.GetPathRoot(target)!;
            var current = volume;
            var parts = Path.GetRelativePath(volume, Path.GetDirectoryName(target)!).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            // Denying directory delete-sharing prevents an ancestor rename between validation and use.
            handles.Add(OpenHandle(current, directory: true));
            foreach (var part in parts)
            {
                current = Path.Combine(current, part);
                handles.Add(OpenHandle(current, directory: true));
            }

            var file = OpenHandle(target, directory: false);
            handles.Add(file);
            var finalPath = FinalPath(file);
            if (!string.Equals(finalPath, target, StringComparison.OrdinalIgnoreCase))
                throw new LauncherOperationException("AI_FILE_IDENTITY_CHANGED", "The opened file's final identity differs from the selected scoped path.");
            var stream = new FileStream(file, FileAccess.Read, 4096, isAsync: false);
            handles.Remove(file);
            return new(stream, handles);
        }
        catch
        {
            foreach (var handle in handles) handle.Dispose();
            throw;
        }
    }

    private static SafeFileHandle OpenHandle(string path, bool directory)
    {
        var access = directory ? (uint)FILE_ACCESS_RIGHTS.FILE_READ_ATTRIBUTES : (uint)GENERIC_ACCESS_RIGHTS.GENERIC_READ;
        var share = FILE_SHARE_MODE.FILE_SHARE_READ | (directory ? FILE_SHARE_MODE.FILE_SHARE_WRITE : 0);
        var flags = FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_OPEN_REPARSE_POINT
            | (directory ? FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS : 0);
        var handle = PInvoke.CreateFile(path, access, share, null, FILE_CREATION_DISPOSITION.OPEN_EXISTING, flags, null);
        if (handle.IsInvalid)
        {
            var code = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new IOException("Cannot open the selected scoped path.", new Win32Exception(code));
        }

        try
        {
            FILE_ATTRIBUTE_TAG_INFO attributes = default;
            if (!PInvoke.GetFileInformationByHandleEx(handle, FILE_INFO_BY_HANDLE_CLASS.FileAttributeTagInfo,
                MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref attributes, 1))))
            {
                throw new IOException("Cannot inspect the opened scoped path.", new Win32Exception(Marshal.GetLastPInvokeError()));
            }

            if ((attributes.FileAttributes & (uint)FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_REPARSE_POINT) != 0)
                throw new LauncherOperationException("AI_FILE_LINK_NOT_ALLOWED", "A scoped path contains a symbolic link, junction or other reparse point. Select its real local directory instead.");
            if ((attributes.FileAttributes & (uint)FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_DIRECTORY) != 0 != directory)
                throw new LauncherOperationException("AI_FILE_KIND_CHANGED", "A scoped path component changed its file/directory kind.");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    private static string FinalPath(SafeFileHandle file)
    {
        var buffer = new char[32768];
        var count = PInvoke.GetFinalPathNameByHandle(file, buffer, GETFINALPATHNAMEBYHANDLE_FLAGS.VOLUME_NAME_DOS);
        if (count == 0 || count >= buffer.Length)
            throw new IOException("Cannot resolve the opened file's final path.", new Win32Exception(Marshal.GetLastPInvokeError()));
        var path = new string(buffer, 0, (int)count);
        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    }

    /// <summary>Owns a file stream and the directory identities pinned for its lifetime.</summary>
    public sealed class Scope(FileStream stream, List<SafeFileHandle> directories) : IDisposable
    {
        /// <summary>Gets the opened file stream whose lifetime is owned by this scope.</summary>
        public FileStream Stream { get; } = stream;
        /// <summary>Closes the file, then releases its ancestor handles from leaf to root.</summary>
        public void Dispose()
        {
            Stream.Dispose();
            foreach (var directory in directories.AsEnumerable().Reverse()) directory.Dispose();
        }
    }

    /// <summary>Owns a validated directory and the ancestor handles that prevent its replacement.</summary>
    public sealed class DirectoryScope(string path, List<SafeFileHandle> directories) : IDisposable
    {
        /// <summary>Gets the validated local directory path.</summary>
        public string Path { get; } = path;
        /// <summary>Releases pinned directory identities from leaf to root.</summary>
        public void Dispose()
        {
            foreach (var directory in directories.AsEnumerable().Reverse()) directory.Dispose();
        }
    }

}