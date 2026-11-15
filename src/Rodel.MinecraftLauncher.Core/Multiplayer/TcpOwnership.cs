// Copyright (c) Richasy. All rights reserved.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.NetworkManagement.IpHelper;

namespace Rodel.MinecraftLauncher.Core.Multiplayer;

internal static class TcpOwnership
{
    internal static IReadOnlyList<(int Port, int Process)> Listeners()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Multiplayer process ownership requires Windows.");
        var result = new HashSet<(int, int)>();
        Read(2, result);
        Read(23, result);
        return [.. result];
    }

    private static unsafe void Read(uint family, HashSet<(int, int)> result)
    {
        uint length = 0;
        _ = PInvoke.GetExtendedTcpTable(null, &length, false, family, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_LISTENER, 0);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var data = NativeMemory.Alloc(length);
            try
            {
                var error = PInvoke.GetExtendedTcpTable(data, &length, false, family, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_LISTENER, 0);
                if ((uint)error == 122)
                    continue;
                if ((uint)error != 0)
                    throw new IOException("Could not verify TCP listener ownership: " + error);
                var count = *(uint*)data;
                var rowSize = family == 2 ? sizeof(MIB_TCPROW_OWNER_PID) : sizeof(MIB_TCP6ROW_OWNER_PID);
                if (length < 4 || count > (length - 4) / rowSize)
                    throw new InvalidDataException("Invalid TCP owner table.");
                for (var index = 0; index < count; index++)
                {
                    var row = (byte*)data + 4 + (index * rowSize);
                    if (family == 2)
                    {
                        var value = *(MIB_TCPROW_OWNER_PID*)row;
                        result.Add((BinaryPrimitives.ReverseEndianness((ushort)value.dwLocalPort), checked((int)value.dwOwningPid)));
                    }
                    else
                    {
                        var value = *(MIB_TCP6ROW_OWNER_PID*)row;
                        result.Add((BinaryPrimitives.ReverseEndianness((ushort)value.dwLocalPort), checked((int)value.dwOwningPid)));
                    }
                }

                return;
            }
            finally
            {
                NativeMemory.Free(data);
            }
        }

        throw new IOException("TCP listeners changed while checking ownership; retry the operation.");
    }
}