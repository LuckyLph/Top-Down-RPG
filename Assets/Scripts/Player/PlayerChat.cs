using Unity.Netcode;
using UnityEngine;
using VContainer;

/// <summary>
/// A player's chat messages (Docs/UiRevamp.md, Phase 12). Its owner sends a line to the host as an RPC; the host cleans
/// it again (<see cref="ChatText.Clean"/>: the fonts' characters, at most 80), names the sender's slot
/// (<see cref="PlayerSlot"/>) and relays it to every machine, its own included, where it goes into the
/// <see cref="ChatLog"/>. The host's own lines go the same way. A line from a player without a slot, or empty once
/// cleaned, goes no further. Nothing is saved, and a late joiner sees only the lines sent after it joined. Offline the
/// player is never spawned, so it sends nothing.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerSlot))]
public class PlayerChat : NetworkBehaviour
{
    private PlayerSlot slot;
    private ChatLog log;

    /// <summary>
    /// Caches the player's slot.
    /// </summary>
    private void Awake()
    {
        slot = GetComponent<PlayerSlot>();
    }

    /// <summary>
    /// Takes this machine's chat log, where relayed lines go.
    /// </summary>
    [Inject]
    public void Construct(ChatLog chatLog)
    {
        log = chatLog;
    }

    /// <summary>
    /// On the owner: sends <paramref name="text"/>, cleaned, to the host. Returns false, sending nothing, when the
    /// player is not spawned or not this machine's, or the text is empty once cleaned.
    /// </summary>
    public bool Say(string text)
    {
        string clean = ChatText.Clean(text);
        if (!IsSpawned || !IsOwner || clean.Length == 0)
        {
            return false;
        }

        SayRpc(clean);
        return true;
    }

    /// <summary>
    /// Runs on the host: cleans the owner's line and relays it to every machine with the player's slot.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SayRpc(string text)
    {
        string clean = ChatText.Clean(text);
        int number = slot != null ? slot.Number : 0;
        if (clean.Length == 0 || !PlayerSlots.IsSlot(number))
        {
            return;
        }

        LineRpc((byte)number, clean);
    }

    /// <summary>
    /// Runs on every machine: adds the host's relayed line from slot <paramref name="number"/> to the chat log.
    /// </summary>
    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
    private void LineRpc(byte number, string text)
    {
        log?.AddPlayerLine(number, ChatText.Clean(text));
    }
}
