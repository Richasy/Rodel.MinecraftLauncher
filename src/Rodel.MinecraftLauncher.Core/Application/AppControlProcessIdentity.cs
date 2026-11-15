// Copyright (c) Richasy. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Rodel.MinecraftLauncher.Core.Application;

internal static class AppControlProcessIdentity
{
    internal static string PackageFor(Process process)
    {
        uint length = 0;
        var error = PInvoke.GetPackageFullName(process.SafeHandle, ref length, []);
        if (error == WIN32_ERROR.APPMODEL_ERROR_NO_PACKAGE) return "unpackaged";
        if (error != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER || length is 0 or > 4096) throw new Win32Exception((int)error);
        var buffer = new char[length];
        error = PInvoke.GetPackageFullName(process.SafeHandle, ref length, buffer);
        if (error != WIN32_ERROR.ERROR_SUCCESS) throw new Win32Exception((int)error);

        return new string(buffer, 0, checked((int)length) - 1);
    }

}