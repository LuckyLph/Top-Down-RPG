using UnityEngine;

/// <summary>
/// Connection settings for online play: the default join address, the host's listen address and port, how long a
/// client waits to connect, and the most players a session admits.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Network Settings", fileName = "NetworkSettings")]
public class NetworkSettings : ScriptableObject
{
    [SerializeField, Tooltip("Address a client connects to when the join field is left empty.")]
    private string defaultAddress = "127.0.0.1";

    [SerializeField, Tooltip("Address the host listens on. 0.0.0.0 accepts connections from the local network.")]
    private string listenAddress = "0.0.0.0";

    [SerializeField, Tooltip("UDP port the host listens on and clients connect to.")]
    private ushort port = 7777;

    [SerializeField, Min(0.5f), Tooltip("Seconds a client waits for the host before giving up.")]
    private float connectTimeoutSeconds = 10f;

    [SerializeField, Range(1, 4), Tooltip("Most players in one session, the host included. The host refuses clients beyond it.")]
    private int maxPlayers = 4;

    public string DefaultAddress => defaultAddress;
    public string ListenAddress => listenAddress;
    public ushort Port => port;
    public float ConnectTimeoutSeconds => connectTimeoutSeconds;
    public int MaxPlayers => maxPlayers;
}
