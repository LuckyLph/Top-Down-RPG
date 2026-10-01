using UnityEngine;

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

    public string DefaultAddress => defaultAddress;
    public string ListenAddress => listenAddress;
    public ushort Port => port;
    public float ConnectTimeoutSeconds => connectTimeoutSeconds;
}
