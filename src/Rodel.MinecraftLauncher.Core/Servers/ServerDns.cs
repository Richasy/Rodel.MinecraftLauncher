// Copyright (c) Richasy. All rights reserved.

using System.Globalization;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.Dns;

namespace Rodel.MinecraftLauncher.Core.Servers;

/// <summary>A server endpoint discovered from an SRV record.</summary>
/// <param name="Target">The record's target hostname, retaining its original Unicode text.</param>
/// <param name="Port">The advertised service port.</param>
/// <param name="Priority">Lower values are preferred before weighted selection.</param>
/// <param name="Weight">Relative selection weight within the same priority.</param>
/// <param name="Ttl">Resolver-provided lifetime in seconds.</param>
public sealed record ServerSrvRecord(string Target, int Port, int Priority, int Weight, uint Ttl = 0);

/// <summary>Uses the Windows resolver, including its configured DNS servers and cache.</summary>
public static class ServerDns
{
    /// <summary>Queries SRV records through the Windows resolver without blocking the calling thread.</summary>
    /// <param name="host">The Unicode or ASCII server hostname, with an optional trailing dot.</param>
    /// <param name="ct">Cancels scheduling or waiting; the underlying synchronous DNS request may still complete.</param>
    /// <returns>The discovered records, or an empty list for an ordinary host with no SRV records.</returns>
    public static Task<IReadOnlyList<ServerSrvRecord>> QueryAsync(string host, CancellationToken ct = default) => Task.Run<IReadOnlyList<ServerSrvRecord>>(() => Query(new IdnMapping().GetAscii(host.TrimEnd('.'))), ct).WaitAsync(ct);
    private static unsafe List<ServerSrvRecord> Query(string host)
    {
        var status = PInvoke.DnsQuery_W("_minecraft._tcp." + host, (ushort)PInvoke.DNS_TYPE_SRV, default, out var records);
        try
        {
            if (status is WIN32_ERROR.DNS_ERROR_RCODE_NAME_ERROR or ((WIN32_ERROR)PInvoke.DNS_INFO_NO_RECORDS))
                return []; // NXDOMAIN / no records: ordinary host remains usable.
            if (status != WIN32_ERROR.ERROR_SUCCESS)
                throw new System.ComponentModel.Win32Exception((int)status);
            return ReadRecords(records);
        }
        finally
        {
            if (records != null)
                PInvoke.DnsFree(records, DNS_FREE_TYPE.DnsFreeRecordList);
        }
    }

    internal static unsafe List<ServerSrvRecord> ReadRecords(DNS_RECORDA* records)
    {
        var result = new List<ServerSrvRecord>();
        // DnsQuery_W returns UTF-16 strings, although metadata exposes its PDNS_RECORD alias as DNS_RECORDA.
        for (var record = (DNS_RECORDW*)records; record != null; record = record->pNext)
        {
            if (record->wType == PInvoke.DNS_TYPE_SRV)
            {
                var srv = record->Data.SRV;
                result.Add(new(srv.pNameTarget.ToString() ?? string.Empty, srv.wPort, srv.wPriority, srv.wWeight, record->dwTtl));
            }
        }

        return result;
    }

    /// <summary>Orders SRV endpoints by priority and weighted selection, preserving all candidates.</summary>
    public static IReadOnlyList<ServerSrvRecord> Order(IEnumerable<ServerSrvRecord> records, Random? random = null)
    {
        random ??= Random.Shared;
        var result = new List<ServerSrvRecord>();
        foreach (var group in records.GroupBy(x => x.Priority).OrderBy(x => x.Key))
        {
            var pending = group.OrderBy(x => x.Weight == 0 ? 0 : 1).ToList();
            while (pending.Count > 0)
            {
                var sum = pending.Sum(x => (long)x.Weight);
                var ticket = sum == 0 ? 0 : random.NextInt64(sum + 1);
                var index = sum == 0 ? random.Next(pending.Count) : 0;
                if (sum > 0)
                {
                    long running = 0;
                    for (; index < pending.Count - 1; index++)
                    {
                        running += pending[index].Weight;
                        if (running >= ticket)
                            break;
                    }
                }

                result.Add(pending[index]);
                pending.RemoveAt(index);
            }
        }

        return result;
    }

}