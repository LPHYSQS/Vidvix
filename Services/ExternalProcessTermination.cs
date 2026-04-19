using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services;

internal static class ExternalProcessTermination
{
    private static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly IntPtr InvalidSnapshotHandle = new(-1);
    private const uint Th32csSnapProcess = 0x00000002;

    public static CancellationTokenRegistration RegisterTermination(Process process, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(process);

        return cancellationToken.CanBeCanceled
            ? cancellationToken.Register(static state => TryTerminateProcess((Process)state!), process)
            : default;
    }

    public static void TryTerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    public static async Task WaitForTerminationAsync(
        Process process,
        int processId,
        ILogger? logger = null,
        string? timeoutWarningMessage = null,
        TimeSpan? gracePeriod = null)
    {
        var trackedProcessIds = CaptureTrackedProcessIds(processId);
        TryTerminateProcess(process);

        if (!AreAnyTrackedProcessesVisible(trackedProcessIds))
        {
            return;
        }

        var waitUntil = DateTimeOffset.UtcNow + (gracePeriod ?? DefaultGracePeriod);

        while (DateTimeOffset.UtcNow < waitUntil)
        {
            if (!AreAnyTrackedProcessesVisible(trackedProcessIds))
            {
                return;
            }

            await Task.Delay(PollInterval).ConfigureAwait(false);
        }

        if (logger is not null && !string.IsNullOrWhiteSpace(timeoutWarningMessage))
        {
            logger.Log(LogLevel.Warning, timeoutWarningMessage);
        }
    }

    private static HashSet<int> CaptureTrackedProcessIds(int processId)
    {
        var trackedProcessIds = new HashSet<int>();
        if (processId <= 0)
        {
            return trackedProcessIds;
        }

        trackedProcessIds.Add(processId);

        try
        {
            var childProcessMap = BuildChildProcessMap();
            var pendingProcessIds = new Queue<int>();
            pendingProcessIds.Enqueue(processId);

            while (pendingProcessIds.Count > 0)
            {
                var currentProcessId = pendingProcessIds.Dequeue();
                if (!childProcessMap.TryGetValue(currentProcessId, out var childProcessIds))
                {
                    continue;
                }

                foreach (var childProcessId in childProcessIds)
                {
                    if (trackedProcessIds.Add(childProcessId))
                    {
                        pendingProcessIds.Enqueue(childProcessId);
                    }
                }
            }
        }
        catch
        {
        }

        return trackedProcessIds;
    }

    private static Dictionary<int, List<int>> BuildChildProcessMap()
    {
        var childProcessMap = new Dictionary<int, List<int>>();
        var snapshotHandle = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshotHandle == InvalidSnapshotHandle)
        {
            return childProcessMap;
        }

        try
        {
            var processEntry = CreateProcessEntry();
            if (!Process32First(snapshotHandle, ref processEntry))
            {
                return childProcessMap;
            }

            do
            {
                var parentProcessId = unchecked((int)processEntry.th32ParentProcessID);
                var childProcessId = unchecked((int)processEntry.th32ProcessID);

                if (!childProcessMap.TryGetValue(parentProcessId, out var childProcessIds))
                {
                    childProcessIds = new List<int>();
                    childProcessMap[parentProcessId] = childProcessIds;
                }

                childProcessIds.Add(childProcessId);
                processEntry.dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>();
            }
            while (Process32Next(snapshotHandle, ref processEntry));
        }
        finally
        {
            _ = CloseHandle(snapshotHandle);
        }

        return childProcessMap;
    }

    private static bool AreAnyTrackedProcessesVisible(HashSet<int> trackedProcessIds)
    {
        if (trackedProcessIds.Count == 0)
        {
            return false;
        }

        foreach (var snapshot in Process.GetProcesses())
        {
            try
            {
                if (trackedProcessIds.Contains(snapshot.Id))
                {
                    return true;
                }
            }
            catch
            {
            }
            finally
            {
                snapshot.Dispose();
            }
        }

        return false;
    }

    private static PROCESSENTRY32 CreateProcessEntry() =>
        new()
        {
            dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>()
        };

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 processEntry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 processEntry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }
}
