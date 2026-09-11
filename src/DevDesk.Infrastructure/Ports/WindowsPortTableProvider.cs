using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using DevDesk.Infrastructure.Ports.Native;

namespace DevDesk.Infrastructure.Ports;

/// <summary>
/// Production implementation querying Windows IP Helper API (GetExtendedTcpTable)
/// to enumerate active TCP listeners and owning PIDs.
/// </summary>
internal sealed class WindowsPortTableProvider : IWindowsPortTableProvider
{
    private readonly ILogger<WindowsPortTableProvider> _logger;

    public WindowsPortTableProvider(ILogger<WindowsPortTableProvider> logger)
    {
        _logger = logger;
    }

    public PortTableResult GetListeners(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (ipv4Listeners, ipv4Success, ipv4Error) = QueryIpv4Listeners(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var (ipv6Listeners, ipv6Success, ipv6Error) = QueryIpv6Listeners(cancellationToken);

        if (ipv4Success && ipv6Success)
        {
            var combined = new List<RawTcpListener>(ipv4Listeners.Count + ipv6Listeners.Count);
            combined.AddRange(ipv4Listeners);
            combined.AddRange(ipv6Listeners);
            return PortTableResult.Ok(combined);
        }

        if (ipv4Success && !ipv6Success)
        {
            _logger.LogWarning("IPv4 TCP listeners were successfully retrieved, but IPv6 listener inspection failed: {Error}", ipv6Error);
            return PortTableResult.Ok(ipv4Listeners, partialFailure: true, warning: $"IPv6 listeners could not be inspected: {ipv6Error}");
        }

        if (!ipv4Success && ipv6Success)
        {
            _logger.LogWarning("IPv6 TCP listeners were successfully retrieved, but IPv4 listener inspection failed: {Error}", ipv4Error);
            return PortTableResult.Ok(ipv6Listeners, partialFailure: true, warning: $"IPv4 listeners could not be inspected: {ipv4Error}");
        }

        _logger.LogError("Failed to inspect both IPv4 and IPv6 TCP listener tables. IPv4 error: {IPv4Err}; IPv6 error: {IPv6Err}", ipv4Error, ipv6Error);
        return PortTableResult.Failed($"Failed to query Windows TCP listener table. IPv4: {ipv4Error}; IPv6: {ipv6Error}");
    }

    private (List<RawTcpListener> Listeners, bool Success, string? Error) QueryIpv4Listeners(CancellationToken cancellationToken)
    {
        var listeners = new List<RawTcpListener>();
        int bufferSize = 0;

        // 1. First probe for buffer size
        uint ret = NativeMethods.GetExtendedTcpTable(
            IntPtr.Zero,
            ref bufferSize,
            false,
            NativeMethods.AF_INET,
            TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_LISTENER,
            0);

        if (ret == NativeMethods.ERROR_NO_DATA)
        {
            return (listeners, true, null);
        }

        if (ret != NativeMethods.ERROR_INSUFFICIENT_BUFFER || bufferSize <= 0)
        {
            return (listeners, false, $"Native call returned code {ret}");
        }

        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            ret = NativeMethods.GetExtendedTcpTable(
                buffer,
                ref bufferSize,
                false,
                NativeMethods.AF_INET,
                TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_LISTENER,
                0);

            if (ret != NativeMethods.ERROR_SUCCESS)
            {
                return (listeners, false, $"Native call returned code {ret}");
            }

            int numEntries = Marshal.ReadInt32(buffer);
            IntPtr rowPtr = IntPtr.Add(buffer, sizeof(int));
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            for (int i = 0; i < numEntries; i++)
            {
                if (i % 100 == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                int port = PortByteOrderHelper.NetworkToHostOrder(row.dwLocalPort);
                var ip = new IPAddress(BitConverter.GetBytes(row.dwLocalAddr)).ToString();

                listeners.Add(new RawTcpListener(
                    port,
                    $"{ip}:{port}",
                    (int)row.dwOwningPid,
                    IsIPv6: false));

                rowPtr = IntPtr.Add(rowPtr, rowSize);
            }

            return (listeners, true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (listeners, false, ex.Message);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private (List<RawTcpListener> Listeners, bool Success, string? Error) QueryIpv6Listeners(CancellationToken cancellationToken)
    {
        var listeners = new List<RawTcpListener>();
        int bufferSize = 0;

        uint ret = NativeMethods.GetExtendedTcpTable(
            IntPtr.Zero,
            ref bufferSize,
            false,
            NativeMethods.AF_INET6,
            TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_LISTENER,
            0);

        if (ret == NativeMethods.ERROR_NO_DATA)
        {
            return (listeners, true, null);
        }

        if (ret != NativeMethods.ERROR_INSUFFICIENT_BUFFER || bufferSize <= 0)
        {
            return (listeners, false, $"Native call returned code {ret}");
        }

        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            ret = NativeMethods.GetExtendedTcpTable(
                buffer,
                ref bufferSize,
                false,
                NativeMethods.AF_INET6,
                TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_LISTENER,
                0);

            if (ret != NativeMethods.ERROR_SUCCESS)
            {
                return (listeners, false, $"Native call returned code {ret}");
            }

            int numEntries = Marshal.ReadInt32(buffer);
            IntPtr rowPtr = IntPtr.Add(buffer, sizeof(int));
            int rowSize = Marshal.SizeOf<MIB_TCP6ROW_OWNER_PID>();

            for (int i = 0; i < numEntries; i++)
            {
                if (i % 100 == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var row = Marshal.PtrToStructure<MIB_TCP6ROW_OWNER_PID>(rowPtr);
                int port = PortByteOrderHelper.NetworkToHostOrder(row.dwLocalPort);
                var ip = new IPAddress(row.ucLocalAddr, row.dwLocalScopeId).ToString();

                listeners.Add(new RawTcpListener(
                    port,
                    $"[{ip}]:{port}",
                    (int)row.dwOwningPid,
                    IsIPv6: true));

                rowPtr = IntPtr.Add(rowPtr, rowSize);
            }

            return (listeners, true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (listeners, false, ex.Message);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
