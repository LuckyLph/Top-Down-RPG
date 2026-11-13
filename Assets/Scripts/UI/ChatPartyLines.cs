using System;
using System.Collections.Generic;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point that raises the chat's lines about the party on this machine (Docs/UiRevamp.md, Phase 12),
/// from what it already knows, with nothing sent: "Player 2 joined" when that player's joining ends
/// (<see cref="PlayerSlot.IsJoining"/>), "Player 2 left" when its player leaves the registry, and "Player 2 is down" when
/// it dies here (a client's copy dies with the host's replicated hit). The local player is left out, since its own
/// death has the notice line, and so is a player without a slot, which every player is offline. A player found already
/// joined, as a late joiner finds the others, raises no joined line. Every text is built once.
/// </summary>
public sealed class ChatPartyLines : IStartable, IDisposable
{
    public const string JoinedFormat = "{0} joined";
    public const string LeftFormat = "{0} left";
    public const string DownFormat = "{0} is down";

    private readonly IPlayerRegistry players;
    private readonly LocalPlayerTracker localPlayer;
    private readonly ChatLog log;
    private readonly List<Member> members = new(PlayerSlots.Count);
    private readonly string[] joined = new string[PlayerSlots.Count + 1];
    private readonly string[] left = new string[PlayerSlots.Count + 1];
    private readonly string[] down = new string[PlayerSlots.Count + 1];
    private bool started;

    /// <summary>
    /// Follows <paramref name="registry"/>'s players, leaves out the one <paramref name="tracker"/> names, and adds the
    /// lines to <paramref name="chatLog"/>.
    /// </summary>
    public ChatPartyLines(IPlayerRegistry registry, LocalPlayerTracker tracker, ChatLog chatLog)
    {
        players = registry;
        localPlayer = tracker;
        log = chatLog;
        for (int number = 1; number <= PlayerSlots.Count; number++)
        {
            joined[number] = string.Format(JoinedFormat, PlayerSlots.Label(number));
            left[number] = string.Format(LeftFormat, PlayerSlots.Label(number));
            down[number] = string.Format(DownFormat, PlayerSlots.Label(number));
        }
    }

    /// <summary>
    /// Follows the registry and the players already in it.
    /// </summary>
    public void Start()
    {
        players.PlayerAdded += Add;
        players.PlayerRemoved += Remove;
        started = true;
        IReadOnlyList<PlayerHandle> party = players.Players;
        for (int i = 0; i < party.Count; i++)
        {
            Add(party[i]);
        }
    }

    /// <summary>
    /// Stops following the registry and every player.
    /// </summary>
    public void Dispose()
    {
        if (!started)
        {
            return;
        }

        started = false;
        players.PlayerAdded -= Add;
        players.PlayerRemoved -= Remove;
        for (int i = members.Count - 1; i >= 0; i--)
        {
            Unfollow(members[i]);
        }

        members.Clear();
    }

    /// <summary>
    /// Starts following <paramref name="player"/>'s slot and death, noting whether it is joining.
    /// </summary>
    private void Add(PlayerHandle player)
    {
        if (player == null || player.Transform == null || IndexOf(player) >= 0)
        {
            return;
        }

        player.Transform.TryGetComponent(out PlayerSlot slot);
        Member member = new(player, slot);
        if (slot != null)
        {
            slot.Changed += HandleSlotChanged;
        }

        if (player.Health != null)
        {
            player.Health.Died += HandleDied;
        }

        members.Add(member);
    }

    /// <summary>
    /// Stops following <paramref name="player"/> and says it left.
    /// </summary>
    private void Remove(PlayerHandle player)
    {
        int index = IndexOf(player);
        if (index < 0)
        {
            return;
        }

        Member member = members[index];
        members.RemoveAt(index);
        Unfollow(member);
        Raise(member, left);
    }

    /// <summary>
    /// Says a player joined when its joining ends, and keeps the last slot it had for when it leaves.
    /// </summary>
    private void HandleSlotChanged(PlayerSlot slot)
    {
        for (int i = 0; i < members.Count; i++)
        {
            Member member = members[i];
            if (member.Slot != slot || !PlayerSlots.IsSlot(slot.Number))
            {
                continue;
            }

            bool wasJoining = member.Joining;
            member.Number = slot.Number;
            member.Joining = slot.IsJoining;
            if (wasJoining && !member.Joining)
            {
                Raise(member, joined);
            }
        }
    }

    /// <summary>
    /// Says a player is down when it dies here.
    /// </summary>
    private void HandleDied(Health health)
    {
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].Handle.Health == health)
            {
                Raise(members[i], down);
            }
        }
    }

    /// <summary>
    /// Adds <paramref name="member"/>'s line of <paramref name="texts"/> to the log, unless it is the local player or has
    /// no slot.
    /// </summary>
    private void Raise(Member member, string[] texts)
    {
        LocalPlayer local = localPlayer.Current;
        if (!PlayerSlots.IsSlot(member.Number) || (local != null && local.Transform == member.Handle.Transform))
        {
            return;
        }

        log.AddPartyLine(texts[member.Number]);
    }

    /// <summary>
    /// Stops following <paramref name="member"/>'s slot and death.
    /// </summary>
    private void Unfollow(Member member)
    {
        if (member.Slot != null)
        {
            member.Slot.Changed -= HandleSlotChanged;
        }

        if (member.Handle.Health != null)
        {
            member.Handle.Health.Died -= HandleDied;
        }
    }

    /// <summary>
    /// Where <paramref name="player"/> is among the followed players, or -1.
    /// </summary>
    private int IndexOf(PlayerHandle player)
    {
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].Handle == player)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// A player this source follows: its handle and slot, the last slot number it had and whether it was joining.
    /// </summary>
    private sealed class Member
    {
        /// <summary>
        /// Follows <paramref name="handle"/> with its <paramref name="slot"/>, as it is now.
        /// </summary>
        public Member(PlayerHandle handle, PlayerSlot slot)
        {
            Handle = handle;
            Slot = slot;
            Number = slot != null ? slot.Number : 0;
            Joining = slot != null && slot.IsJoining;
        }

        public PlayerHandle Handle { get; }
        public PlayerSlot Slot { get; }
        public int Number { get; set; }
        public bool Joining { get; set; }
    }
}
