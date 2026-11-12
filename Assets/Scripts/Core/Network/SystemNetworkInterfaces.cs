using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;

/// <summary>
/// The machine's own network interfaces, from <see cref="NetworkInterface.GetAllNetworkInterfaces"/>. Reading them
/// allocates, so it is done when a screen opens, never per frame. An interface the platform cannot describe is skipped,
/// and a platform that cannot list them gives none, each with a warning.
/// </summary>
public sealed class SystemNetworkInterfaces : INetworkInterfaceSource
{
    /// <summary>
    /// Clears <paramref name="interfaces"/> and fills it with each interface's name, state, gateway and IPv4 addresses.
    /// </summary>
    public void Read(List<NetworkInterfaceInfo> interfaces)
    {
        interfaces.Clear();
        NetworkInterface[] all;
        try
        {
            all = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (Exception exception) when (exception is NetworkInformationException || exception is PlatformNotSupportedException)
        {
            Debug.LogWarning($"This machine's network interfaces could not be read ({exception.Message}).");
            return;
        }

        foreach (NetworkInterface item in all)
        {
            try
            {
                IPInterfaceProperties properties = item.GetIPProperties();
                List<IPAddress> addresses = new();
                foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        addresses.Add(unicast.Address);
                    }
                }

                bool hasGateway = false;
                foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses)
                {
                    if (gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any))
                    {
                        hasGateway = true;
                    }
                }

                interfaces.Add(new NetworkInterfaceInfo(
                    item.Name,
                    item.Description,
                    item.OperationalStatus == OperationalStatus.Up,
                    item.NetworkInterfaceType == NetworkInterfaceType.Loopback,
                    hasGateway,
                    addresses));
            }
            catch (Exception exception) when (exception is NetworkInformationException || exception is PlatformNotSupportedException)
            {
                Debug.LogWarning($"The network interface '{item.Name}' could not be read ({exception.Message}).");
            }
        }
    }
}
