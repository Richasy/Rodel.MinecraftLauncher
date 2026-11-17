// Copyright (c) Richasy. All rights reserved.

using Microsoft.Win32;
using Sprout;
using System.Runtime.InteropServices;

namespace Rodel.MinecraftLauncher.App.Settings;

/// <summary>Holds the installed process's running marker so removal cannot proceed while it remains alive.</summary>
internal sealed class InstallationLifetime : IDisposable
{
    private static readonly System.Buffers.SearchValues<char> s_myChars = System.Buffers.SearchValues.Create("0123456789abcdefABCDEF");
    private readonly Mutex _running;

    private InstallationLifetime(string name) => _running = new(false, name);

    internal static InstallationLifetime? Open()
    {
        if (PackageIdentity.IsPackaged)
            return null;
        using var registry = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var installation = registry.OpenSubKey(@"Software\Richasy\RMCL\Installations\" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
        if (installation?.GetValue("Path") is not string path
            || !string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase)
            || installation.GetValue("Mutex") is not string name
            || !name.StartsWith(@"Local\Richasy.RMCL.", StringComparison.Ordinal)
            || name.Length != @"Local\Richasy.RMCL.".Length + 64
            || name.AsSpan(@"Local\Richasy.RMCL.".Length).ContainsAnyExcept(s_myChars))
        {
            return null;
        }

        return new(name);
    }

    /// <summary>Releases the running marker after the application's run loop ends.</summary>
    public void Dispose() => _running.Dispose();
}