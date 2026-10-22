using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;

namespace ShieldBlock.Core.Network
{
    public class ProcessInfo
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public DateTime ResolvedAt { get; set; } = DateTime.UtcNow;
    }

    public static class AppConnectionTracker
    {
        private static readonly ConcurrentDictionary<int, ProcessInfo> _processCache = new();
        private static readonly TimeSpan _cacheExpiry = TimeSpan.FromSeconds(30);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable,
            ref int pdwSize,
            bool bOrder,
            int ulAf,
            TCP_TABLE_CLASS tableClass,
            uint reserved = 0);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(
            IntPtr pUdpTable,
            ref int pdwSize,
            bool bOrder,
            int ulAf,
            UDP_TABLE_CLASS tableClass,
            uint reserved = 0);

        private const int AF_INET = 2; // IPv4
        private const int AF_INET6 = 23; // IPv6

        private enum TCP_TABLE_CLASS
        {
            TCP_TABLE_BASIC_LISTENER,
            TCP_TABLE_BASIC_CONNECTIONS,
            TCP_TABLE_BASIC_ALL,
            TCP_TABLE_OWNER_PID_LISTENER,
            TCP_TABLE_OWNER_PID_CONNECTIONS,
            TCP_TABLE_OWNER_PID_ALL,
            TCP_TABLE_OWNER_MODULE_LISTENER,
            TCP_TABLE_OWNER_MODULE_CONNECTIONS,
            TCP_TABLE_OWNER_MODULE_ALL
        }

        private enum UDP_TABLE_CLASS
        {
            UDP_TABLE_BASIC,
            UDP_TABLE_OWNER_PID,
            UDP_TABLE_OWNER_MODULE
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            public byte localPort1;
            public byte localPort2;
            public byte localPort3;
            public byte localPort4;
            public uint remoteAddr;
            public byte remotePort1;
            public byte remotePort2;
            public byte remotePort3;
            public byte remotePort4;
            public uint owningPid;
        }

        public static ProcessInfo? GetProcessForPort(ushort localPort, bool isTcp = true)
        {
            try
            {
                int pid = isTcp ? FindTcpOwnerPid(localPort) : FindUdpOwnerPid(localPort);
                if (pid <= 0) return null;

                return ResolveProcessInfo(pid);
            }
            catch
            {
                return null;
            }
        }

        public static ProcessInfo ResolveProcessInfo(int pid)
        {
            if (_processCache.TryGetValue(pid, out var cached))
            {
                if (DateTime.UtcNow - cached.ResolvedAt < _cacheExpiry)
                {
                    return cached;
                }
            }

            var info = new ProcessInfo
            {
                ProcessId = pid,
                ProcessName = $"PID-{pid}",
                ResolvedAt = DateTime.UtcNow
            };

            try
            {
                using var proc = Process.GetProcessById(pid);
                info.ProcessName = proc.ProcessName;
                try
                {
                    info.ExecutablePath = proc.MainModule?.FileName ?? string.Empty;
                }
                catch
                {
                    // Access denied for system processes like System, Registry, svchost
                    info.ExecutablePath = string.Empty;
                }
            }
            catch
            {
                // Process may have exited
            }

            _processCache[pid] = info;
            return info;
        }

        private static int FindTcpOwnerPid(ushort localPort)
        {
            int bufferSize = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL);

            if (bufferSize <= 0) return 0;

            IntPtr pTable = Marshal.AllocHGlobal(bufferSize);
            try
            {
                uint ret = GetExtendedTcpTable(pTable, ref bufferSize, true, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL);
                if (ret != 0) return 0;

                int numEntries = Marshal.ReadInt32(pTable);
                IntPtr rowPtr = IntPtr.Add(pTable, 4);

                for (int i = 0; i < numEntries; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                    ushort port = (ushort)((row.localPort1 << 8) | row.localPort2);

                    if (port == localPort)
                    {
                        return (int)row.owningPid;
                    }

                    rowPtr = IntPtr.Add(rowPtr, Marshal.SizeOf<MIB_TCPROW_OWNER_PID>());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pTable);
            }

            return 0;
        }

        private static int FindUdpOwnerPid(ushort localPort)
        {
            int bufferSize = 0;
            GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID);

            if (bufferSize <= 0) return 0;

            IntPtr pTable = Marshal.AllocHGlobal(bufferSize);
            try
            {
                uint ret = GetExtendedUdpTable(pTable, ref bufferSize, true, AF_INET, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID);
                if (ret != 0) return 0;

                int numEntries = Marshal.ReadInt32(pTable);
                IntPtr rowPtr = IntPtr.Add(pTable, 4);

                // MIB_UDPROW_OWNER_PID: dwLocalAddr (4), dwLocalPort (4), dwOwningPid (4)
                for (int i = 0; i < numEntries; i++)
                {
                    byte port1 = Marshal.ReadByte(rowPtr, 4);
                    byte port2 = Marshal.ReadByte(rowPtr, 5);
                    ushort port = (ushort)((port1 << 8) | port2);
                    int pid = Marshal.ReadInt32(rowPtr, 8);

                    if (port == localPort)
                    {
                        return pid;
                    }

                    rowPtr = IntPtr.Add(rowPtr, 12);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pTable);
            }

            return 0;
        }

        public static List<ProcessInfo> GetActiveNetworkProcesses()
        {
            var result = new Dictionary<int, ProcessInfo>();
            int bufferSize = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, false, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL);

            if (bufferSize > 0)
            {
                IntPtr pTable = Marshal.AllocHGlobal(bufferSize);
                try
                {
                    if (GetExtendedTcpTable(pTable, ref bufferSize, false, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL) == 0)
                    {
                        int numEntries = Marshal.ReadInt32(pTable);
                        IntPtr rowPtr = IntPtr.Add(pTable, 4);

                        for (int i = 0; i < numEntries; i++)
                        {
                            var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                            int pid = (int)row.owningPid;
                            if (pid > 0 && !result.ContainsKey(pid))
                            {
                                result[pid] = ResolveProcessInfo(pid);
                            }
                            rowPtr = IntPtr.Add(rowPtr, Marshal.SizeOf<MIB_TCPROW_OWNER_PID>());
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pTable);
                }
            }

            return new List<ProcessInfo>(result.Values);
        }
    }
}
