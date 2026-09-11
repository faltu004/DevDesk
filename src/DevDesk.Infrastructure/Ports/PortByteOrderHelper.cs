namespace DevDesk.Infrastructure.Ports;

/// <summary>
/// Converts TCP port integers and byte representations between network byte order (big-endian) and host byte order (little-endian).
/// </summary>
internal static class PortByteOrderHelper
{
    /// <summary>
    /// Converts a 32-bit integer containing a 16-bit network byte order port into a host byte order port number.
    /// In MIB_TCPROW_OWNER_PID and MIB_TCP6ROW_OWNER_PID, dwLocalPort is stored as network order in the lower 16 bits.
    /// </summary>
    public static int NetworkToHostOrder(uint networkPort)
    {
        return (int)(((networkPort & 0xFF) << 8) | ((networkPort >> 8) & 0xFF));
    }

    /// <summary>
    /// Converts a host byte order port number to network byte order uint for testing.
    /// </summary>
    public static uint HostToNetworkOrder(int hostPort)
    {
        return (uint)(((hostPort & 0xFF) << 8) | ((hostPort >> 8) & 0xFF));
    }
}
