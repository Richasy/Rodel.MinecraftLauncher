// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Windows.Win32;
using Windows.Win32.Security.Cryptography;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

internal sealed class AiSecretStore(LauncherPaths paths)
{
    private string DirectoryPath => Path.Combine(paths.Root, "ai", "secrets");

    /// <summary>Stores a current-user DPAPI credential under a private ACL and returns only its opaque reference.</summary>
    public string Save(string secret)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("AI credentials require Windows CurrentUser protection.");
        if (string.IsNullOrWhiteSpace(secret) || secret.Length > 16384 || secret.Any(char.IsControl))
            throw new ArgumentException("The credential must be nonempty, at most 16384 characters and contain no control characters.");

        var security = new DirectorySecurity();
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("The current Windows user has no SID.");
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(user);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        if (Directory.Exists(DirectoryPath))
        {
            if ((File.GetAttributes(DirectoryPath) & FileAttributes.ReparsePoint) != 0)
                throw new LauncherOperationException("AI_SECRET_PATH_INVALID", "The AI secret directory must not be a reparse point.");
            new DirectoryInfo(DirectoryPath).SetAccessControl(security);
        }
        else
        {
            FileSystemAclExtensions.CreateDirectory(security, DirectoryPath);
        }

        var id = Guid.NewGuid().ToString("N");
        var plain = Encoding.UTF8.GetBytes(secret);
        try
        {
            var encrypted = Protect(plain, decrypt: false);
            var file = PathFor("dpapi:" + id);
            try
            {
                using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
                stream.Write(encrypted);
                stream.Flush(flushToDisk: true);
            }
            catch
            {
                if (File.Exists(file))
                    File.Delete(file);
                throw;
            }

            return "dpapi:" + id;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>Opens an environment or protected credential, retaining a read lease until the caller releases it.</summary>
    public AiCredentialLease Open(string? reference)
    {
        if (reference is null)
            return new(null, null);
        if (reference.StartsWith("env:", StringComparison.Ordinal))
        {
            var value = Environment.GetEnvironmentVariable(reference[4..]);
            if (string.IsNullOrWhiteSpace(value))
                throw new LauncherOperationException("AI_CREDENTIAL_UNAVAILABLE", "The configured credential environment variable is missing or empty. Set it before starting the AI host.");
            if (value.Length > 16384 || value.Any(char.IsControl))
                throw new LauncherOperationException("AI_CREDENTIAL_INVALID", "The configured credential must not exceed 16384 characters or contain control characters.");
            return new(value, null);
        }

        FileStream? stream = null;
        try
        {
            var path = PathFor(reference);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new LauncherOperationException("AI_SECRET_PATH_INVALID", "An AI credential file must not be a reparse point.");
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 65536)
                throw new InvalidDataException("The protected credential is too large.");
            var encrypted = new byte[checked((int)stream.Length)];
            stream.ReadExactly(encrypted);
            var plain = Protect(encrypted, decrypt: true);
            try
            {
                return new(Encoding.UTF8.GetString(plain), stream);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
        {
            stream?.Dispose();
            throw new LauncherOperationException("AI_CREDENTIAL_UNAVAILABLE", "The current Windows user cannot read or decrypt this credential. Service metadata remains available; enter the credential again.");
        }
        catch
        {
            stream?.Dispose();
            throw;
        }
    }

    /// <summary>Deletes a protected credential unless an in-flight read lease still owns it.</summary>
    public bool TryDelete(string reference)
    {
        if (!reference.StartsWith("dpapi:", StringComparison.Ordinal))
            return true;
        try
        {
            File.Delete(PathFor(reference));
            return true;
        }
        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
        {
            // An in-flight request retains a shared-read handle until its credential snapshot is released.
            return false;
        }
    }

    private string PathFor(string reference)
    {
        if (!reference.StartsWith("dpapi:", StringComparison.Ordinal) || !Guid.TryParseExact(reference[6..], "N", out _))
            throw new LauncherOperationException("AI_CREDENTIAL_REFERENCE_INVALID", "The AI credential reference is invalid.");
        return Path.Combine(DirectoryPath, reference[6..] + ".bin");
    }

    private static unsafe byte[] Protect(byte[] bytes, bool decrypt)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("AI credentials require Windows CurrentUser protection.");
        fixed (byte* data = bytes)
        {
            var input = new CRYPT_INTEGER_BLOB { cbData = (uint)bytes.Length, pbData = data };
            var success = decrypt
                ? PInvoke.CryptUnprotectData(in input, null, null, PInvoke.CRYPTPROTECT_UI_FORBIDDEN, out var output)
                : PInvoke.CryptProtectData(in input, null, null, null, PInvoke.CRYPTPROTECT_UI_FORBIDDEN, out output);
            if (!success)
                throw new CryptographicException(new Win32Exception(Marshal.GetLastPInvokeError()).Message);
            using var allocation = new LocalFreeSafeHandle((nint)output.pbData);
            var outputBytes = new Span<byte>(output.pbData, checked((int)output.cbData));
            try
            {
                return outputBytes.ToArray();
            }
            finally
            {
                if (decrypt)
                    CryptographicOperations.ZeroMemory(outputBytes);
            }
        }
    }

}

internal sealed class AiCredentialLease : IDisposable
{
    private readonly FileStream? _stream;
    internal AiCredentialLease(string? value, FileStream? stream) => (Value, _stream) = (value, stream);
    /// <summary>Gets the leased credential value; disposal drops this reference and releases the protected file.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Value { get; private set; }
    internal AiCredentialLease Retain() => new(Value, _stream is null ? null
        : new FileStream(_stream.Name, FileMode.Open, FileAccess.Read, FileShare.Read));
    /// <summary>Releases the file lease and drops the plaintext reference without claiming to erase immutable strings.</summary>
    public void Dispose()
    {
        Value = null;
        _stream?.Dispose();
    }
}