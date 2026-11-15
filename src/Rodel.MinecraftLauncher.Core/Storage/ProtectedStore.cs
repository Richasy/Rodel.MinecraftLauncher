// Copyright (c) Richasy. All rights reserved.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Windows.Win32;
using Windows.Win32.Security.Cryptography;

namespace Rodel.MinecraftLauncher.Core.Storage;

/// <summary>Current-Windows-user DPAPI storage. Never falls back to plaintext.</summary>
internal static class ProtectedStore
{
    internal static JsonObject? Read(string path)
    {
        if (!File.Exists(path)) return null;
        var bytes = Transform(Convert.FromBase64String(JsonFiles.ReadObject(path)["protected"]!.GetValue<string>()), false);
        try { return JsonNode.Parse(bytes)!.AsObject(); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    internal static Task WriteAsync(string path, JsonObject value, CancellationToken ct = default)
    {
        var bytes = Encoding.UTF8.GetBytes(value.ToJsonString());
        try { return JsonFiles.WriteAsync(path, new JsonObject { ["protected"] = Convert.ToBase64String(Transform(bytes, true)) }, ct); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static unsafe byte[] Transform(byte[] bytes, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Account credentials require Windows data protection.");
        fixed (byte* data = bytes)
        {
            var input = new CRYPT_INTEGER_BLOB { cbData = (uint)bytes.Length, pbData = data };
            var success = protect
                ? PInvoke.CryptProtectData(in input, null, null, null, PInvoke.CRYPTPROTECT_UI_FORBIDDEN, out var output)
                : PInvoke.CryptUnprotectData(in input, null, null, PInvoke.CRYPTPROTECT_UI_FORBIDDEN, out output);
            if (!success) throw new CryptographicException("Windows could not protect or open account credentials. Sign in again or check the Windows user profile.");
            using var allocation = new LocalFreeSafeHandle((nint)output.pbData);
            var outputBytes = new Span<byte>(output.pbData, checked((int)output.cbData));
            try { return outputBytes.ToArray(); }
            finally
            {
                if (!protect) CryptographicOperations.ZeroMemory(outputBytes);
            }
        }
    }
}