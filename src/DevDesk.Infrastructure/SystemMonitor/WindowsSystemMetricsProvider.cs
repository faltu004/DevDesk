using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using DevDesk.Core.SystemMonitor;
using DevDesk.Infrastructure.SystemMonitor.Native;
using Microsoft.Win32;

namespace DevDesk.Infrastructure.SystemMonitor;

/// <summary>
/// Authoritative Windows implementation of ISystemMetricsProvider using standard-user Win32 and .NET APIs.
/// </summary>
public sealed class WindowsSystemMetricsProvider : ISystemMetricsProvider
{
    private string? _cachedOsDescription;
    private int? _cachedProcessorCount;

    public bool TryGetRawCpuTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime)
    {
        idleTime = 0;
        kernelTime = 0;
        userTime = 0;

        if (!SystemMetricsNativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return false;
        }

        idleTime = idle.ToULong();
        kernelTime = kernel.ToULong();
        userTime = user.ToULong();
        return true;
    }

    public bool TryGetRawMemory(out ulong totalPhysicalBytes, out ulong availablePhysicalBytes)
    {
        totalPhysicalBytes = 0;
        availablePhysicalBytes = 0;

        var memStatus = SystemMetricsNativeMethods.MEMORYSTATUSEX.Create();
        if (!SystemMetricsNativeMethods.GlobalMemoryStatusEx(ref memStatus))
        {
            return false;
        }

        totalPhysicalBytes = memStatus.ullTotalPhys;
        availablePhysicalBytes = memStatus.ullAvailPhys;
        return true;
    }

    public DiskMetrics GetDiskMetrics()
    {
        string? systemDriveRoot = null;
        try
        {
            systemDriveRoot = Path.GetPathRoot(Environment.SystemDirectory);
        }
        catch
        {
            // Ignore failure to resolve system directory
        }

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            return DiskMetrics.Empty();
        }

        var results = new List<DiskDriveMetrics>();
        DiskDriveMetrics? primarySystemDrive = null;

        foreach (var drive in drives)
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed)
                {
                    continue;
                }

                bool isSystem = systemDriveRoot != null &&
                    string.Equals(drive.Name.TrimEnd('\\'), systemDriveRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

                if (!drive.IsReady)
                {
                    results.Add(new DiskDriveMetrics
                    {
                        DriveName = drive.Name,
                        IsReady = false,
                        IsSystemDrive = isSystem
                    });
                    continue;
                }

                ulong totalBytes = (ulong)drive.TotalSize;
                ulong freeBytes = (ulong)drive.AvailableFreeSpace;
                ulong usedBytes = totalBytes >= freeBytes ? totalBytes - freeBytes : 0;
                double? usagePercent = totalBytes > 0
                    ? Math.Clamp((double)usedBytes / totalBytes * 100.0, 0.0, 100.0)
                    : null;

                string volumeLabel = string.Empty;
                try
                {
                    volumeLabel = drive.VolumeLabel;
                }
                catch
                {
                    // Volume label reading can fail on some encrypted or restricted drives
                }

                var metric = new DiskDriveMetrics
                {
                    DriveName = drive.Name,
                    VolumeLabel = volumeLabel,
                    TotalBytes = totalBytes,
                    FreeBytes = freeBytes,
                    UsedBytes = usedBytes,
                    UsagePercentage = usagePercent,
                    IsReady = true,
                    IsSystemDrive = isSystem
                };

                results.Add(metric);

                if (isSystem && primarySystemDrive == null)
                {
                    primarySystemDrive = metric;
                }
            }
            catch
            {
                // Isolate individual drive failures (e.g. BitLocker locked drive or permission error)
            }
        }

        // Fallback: if no drive was marked as primary system drive, pick the first ready fixed drive
        if (primarySystemDrive == null && results.Count > 0)
        {
            primarySystemDrive = results.Find(d => d.IsReady);
        }

        return new DiskMetrics
        {
            Drives = results,
            PrimarySystemDrive = primarySystemDrive
        };
    }

    public IReadOnlyList<RawNetworkAdapterSnapshot> GetNetworkAdapterSnapshots()
    {
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch
        {
            return Array.Empty<RawNetworkAdapterSnapshot>();
        }

        var results = new List<RawNetworkAdapterSnapshot>(interfaces.Length);

        foreach (var ni in interfaces)
        {
            try
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                // Filter out loopback and tunnel adapters
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var stats = ni.GetIPStatistics();
                ulong rx = (ulong)stats.BytesReceived;
                ulong tx = (ulong)stats.BytesSent;

                results.Add(new RawNetworkAdapterSnapshot(
                    ni.Id,
                    ni.Name,
                    rx,
                    tx,
                    IsOperationalUp: true));
            }
            catch
            {
                // Network interface statistics query failure is safely ignored for that adapter
            }
        }

        return results;
    }

    public TimeSpan GetUptime()
    {
        return TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    public string GetOsDescription()
    {
        if (_cachedOsDescription != null)
        {
            return _cachedOsDescription;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (key != null)
                {
                    var productName = key.GetValue("ProductName") as string;
                    var displayVersion = key.GetValue("DisplayVersion") as string;
                    var currentBuild = key.GetValue("CurrentBuild") as string;

                    // Windows 11 often still reports "Windows 10 Pro" in ProductName; check build number
                    if (int.TryParse(currentBuild, out int buildNumber) && buildNumber >= 22000 && productName != null)
                    {
                        productName = productName.Replace("Windows 10", "Windows 11");
                    }

                    if (!string.IsNullOrWhiteSpace(productName))
                    {
                        var parts = new List<string> { productName };
                        if (!string.IsNullOrWhiteSpace(displayVersion))
                        {
                            parts.Add(displayVersion);
                        }
                        if (!string.IsNullOrWhiteSpace(currentBuild))
                        {
                            parts.Add($"Build {currentBuild}");
                        }

                        _cachedOsDescription = string.Join(" ", parts);
                        return _cachedOsDescription;
                    }
                }
            }
        }
        catch
        {
            // Registry read failure falls back to RuntimeInformation
        }

        _cachedOsDescription = RuntimeInformation.OSDescription;
        return _cachedOsDescription;
    }

    public int GetLogicalProcessorCount()
    {
        _cachedProcessorCount ??= Environment.ProcessorCount;
        return _cachedProcessorCount.Value;
    }
}
