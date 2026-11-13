using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VContainer;

/// <summary>
/// A player's slot (Docs/UiRevamp.md, Phase 5): a server-written <see cref="PlayerSlotState"/> on the player object,
/// so every machine agrees on it and a late joiner reads it with the player's spawn. When the host spawns a player it
/// gives it the lowest slot no other registered player holds (its own player the first, so 1); a player who leaves
/// takes its slot with it, which the next one to join gets. A client's player is joining from its spawn until the
/// host hears its machine has the world around it (<see cref="IClientReadiness"/>); the host's own player never is.
/// Offline the player is never spawned, so it has no slot (0).
/// </summary>
[DisallowMultipleComponent]
public class PlayerSlot : NetworkBehaviour
{
    private readonly NetworkVariable<PlayerSlotState> state = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly List<int> taken = new(PlayerSlots.Count);
    private IPlayerRegistry players;
    private IClientReadiness readiness;
    private PlayerSlotState configured;

    /// <summary>
    /// Raised on every machine when the slot or the joining flag changes, and when the player spawns or despawns.
    /// </summary>
    public event Action<PlayerSlot> Changed;

    /// <summary>
    /// The slot's number from 1 to <see cref="PlayerSlots.Count"/>, or 0 without one.
    /// </summary>
    public int Number => Current.Number;

    /// <summary>
    /// Whether the player's machine is still loading the world around it.
    /// </summary>
    public bool IsJoining => Current.Joining;

    private PlayerSlotState Current => IsSpawned ? state.Value : configured;

    /// <summary>
    /// Takes the players whose slots are taken and the host's view of each client's readiness.
    /// </summary>
    [Inject]
    public void Construct(IPlayerRegistry registry, IClientReadiness clientReadiness)
    {
        players = registry;
        readiness = clientReadiness;
    }

    /// <summary>
    /// Follows the replicated slot; on the host, gives the player the lowest free slot, joining until its machine is
    /// ready.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        state.OnValueChanged += HandleReplicated;
        if (IsServer)
        {
            state.Value = new PlayerSlotState(LowestFree(), !IsReady());
        }

        Changed?.Invoke(this);
    }

    /// <summary>
    /// Stops following the replicated slot.
    /// </summary>
    public override void OnNetworkDespawn()
    {
        state.OnValueChanged -= HandleReplicated;
        Changed?.Invoke(this);
    }

    /// <summary>
    /// On the host, clears the joining flag once the player's machine is ready.
    /// </summary>
    private void Update()
    {
        if (!IsServer || !IsSpawned || !state.Value.Joining || !IsReady())
        {
            return;
        }

        state.Value = new PlayerSlotState(state.Value.Number, false);
    }

    /// <summary>
    /// Sets the slot of a player that is not spawned, for tests, and raises <see cref="Changed"/>.
    /// </summary>
    internal void Configure(int number, bool joining)
    {
        configured = new PlayerSlotState(number, joining);
        Changed?.Invoke(this);
    }

    /// <summary>
    /// Whether this player's machine has the world around it: the host's own always has.
    /// </summary>
    private bool IsReady()
    {
        return readiness == null || readiness.IsClientReady(OwnerClientId);
    }

    /// <summary>
    /// The lowest slot no other registered player holds.
    /// </summary>
    private int LowestFree()
    {
        taken.Clear();
        if (players != null)
        {
            IReadOnlyList<PlayerHandle> party = players.Players;
            for (int i = 0; i < party.Count; i++)
            {
                Transform other = party[i].Transform;
                if (other != null && other != transform && other.TryGetComponent(out PlayerSlot slot))
                {
                    taken.Add(slot.Number);
                }
            }
        }

        return PlayerSlots.LowestFree(taken);
    }

    /// <summary>
    /// Raises <see cref="Changed"/> when the replicated state changes.
    /// </summary>
    private void HandleReplicated(PlayerSlotState previous, PlayerSlotState current)
    {
        Changed?.Invoke(this);
    }
}
