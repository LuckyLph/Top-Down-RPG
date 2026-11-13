using System.Collections.Generic;

/// <summary>
/// This machine's network interfaces, read when asked, so <see cref="HostAddresses"/> is tested with interfaces of its
/// own.
/// </summary>
public interface INetworkInterfaceSource
{
    /// <summary>
    /// Clears <paramref name="interfaces"/> and fills it with the interfaces as they are now.
    /// </summary>
    void Read(List<NetworkInterfaceInfo> interfaces);
}
