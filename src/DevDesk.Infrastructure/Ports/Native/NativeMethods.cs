using System.Runtime.InteropServices;

namespace DevDesk.Infrastructure.Ports.Native;

internal enum TCP_TABLE_CLASS
{
    TCP_TABLE_BASIC_LISTENER = 0,
    TCP_TABLE_BASIC_CONNECTIONS = 1,
    TCP_TABLE_BASIC_ALL = 2,
    TCP_TABLE_OWNER_PID_LISTENER = 3,
    TCP_TABLE_OWNER_PID_CONNECTIONS = 4,
    TCP_TABLE_OWNER_PID_ALL = 5,
    TCP_TABLE_OWNER_MODULE_LISTENER = 6,
    TCP_TABLE_OWNER_MODULE_CONNECTIONS = 7,
    TCP_TABLE_OWNER_MODULE_ALL = 8
}

[StructLayout(LayoutKind.Sequential)]
internal struct MIB_TCPROW_OWNER_PID
{
    public uint dwState;
    public uint dwLocalAddr;
    public uint dwLocalPort;
    public uint dwRemoteAddr;
    public uint dwRemotePort;
    public uint dwOwningPid;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MIB_TCP6ROW_OWNER_PID
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] ucLocalAddr;
    public uint dwLocalScopeId;
    public uint dwLocalPort;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] ucRemoteAddr;
    public uint dwRemoteScopeId;
    public uint dwRemotePort;
    public uint dwState;
    public uint dwOwningPid;
}

internal static class NativeMethods
{
    public const int AF_INET = 2;
    public const int AF_INET6 = 23;
    public const uint ERROR_SUCCESS = 0;
    public const uint ERROR_INSUFFICIENT_BUFFER = 122;
    public const uint ERROR_NO_DATA = 232;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int pdwSize,
        [MarshalAs(UnmanagedType.Bool)] bool bOrder,
        int ulAf,
        TCP_TABLE_CLASS tableClass,
        uint reserved);
}
