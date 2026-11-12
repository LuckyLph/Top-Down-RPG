using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point for the HUD's party frames (Docs/UiRevamp.md, Phase 5), on this machine only: one frame
/// per other registered player that has a slot, in slot order, the local player left out, so playing alone none show.
/// Each frame shows, from what this machine already has:
/// <list type="bullet">
/// <item>"Joining" while the player's <see cref="PlayerSlot"/> says its machine is still loading;</item>
/// <item>"In another world" while <see cref="PlayerWorlds"/> puts it in another world than the local player;</item>
/// <item>while it is dead, "Respawning in 3", counting whole seconds down from
/// <see cref="GameplaySettings.RespawnDelaySeconds"/> since this machine saw it die, then "Respawning..." until it is
/// restored;</item>
/// <item>else its health and up to four of its distinct debuffs, in the order they landed.</item>
/// </list>
/// It polls health and statuses, since a client's copies change without events. Every text is built once, so
/// <see cref="Tick"/> allocates nothing.
/// </summary>
public sealed class PartyFramesPresenter : IStartable, ITickable, IDisposable
{
    public const string RespawningFormat = "Respawning in {0}";
    public const string InAnotherWorld = "In another world";
    public const string Joining = "Joining";
    private const int MaxPips = 4;

    private readonly IPlayerRegistry players;
    private readonly LocalPlayerTracker localPlayer;
    private readonly PlayerWorlds playerWorlds;
    private readonly IClock clock;
    private readonly GameplaySettings settings;
    private readonly PartyFramesView view;
    private readonly List<Member> members = new(PlayerSlots.Count);
    private readonly Member[] order = new Member[PlayerSlots.Count];
    private readonly Color[] pipColours = new Color[MaxPips];
    private readonly StatusEffectDefinition[] pipDefinitions = new StatusEffectDefinition[MaxPips];
    private string[] countdown = new string[0];
    private bool started;

    /// <summary>
    /// Captures the players, the local one and their worlds, the game clock, the respawn delay and the frames.
    /// </summary>
    public PartyFramesPresenter(
        IPlayerRegistry players,
        LocalPlayerTracker localPlayer,
        PlayerWorlds playerWorlds,
        IClock clock,
        GameplaySettings settings,
        PartyFramesView view)
    {
        this.players = players;
        this.localPlayer = localPlayer;
        this.playerWorlds = playerWorlds;
        this.clock = clock;
        this.settings = settings;
        this.view = view;
    }

    /// <summary>
    /// How many frames show, for tests.
    /// </summary>
    internal int ShownCount { get; private set; }

    /// <summary>
    /// Builds the countdown's texts, follows the registry and takes the players already in it.
    /// </summary>
    public void Start()
    {
        int seconds = Mathf.Max(0, Mathf.CeilToInt(settings.RespawnDelaySeconds));
        countdown = new string[seconds + 1];
        for (int second = 1; second <= seconds; second++)
        {
            countdown[second] = string.Format(RespawningFormat, second);
        }

        players.PlayerAdded += Add;
        players.PlayerRemoved += Remove;
        started = true;
        IReadOnlyList<PlayerHandle> party = players.Players;
        for (int i = 0; i < party.Count; i++)
        {
            Add(party[i]);
        }

        Tick();
    }

    /// <summary>
    /// Stops following the registry.
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
        members.Clear();
    }

    /// <summary>
    /// Shows a frame for each other player with a slot, in slot order, and hides the rest.
    /// </summary>
    public void Tick()
    {
        if (view == null)
        {
            return;
        }

        PlayerHandle local = localPlayer.Current?.Handle;
        Transform localTransform = local?.Transform;
        WorldDefinition localWorld = local != null ? playerWorlds.Get(local) : null;
        int count = 0;
        for (int i = 0; i < members.Count; i++)
        {
            Member member = members[i];
            member.Track(clock.Time);
            if ((localTransform != null && member.Handle.Transform == localTransform) || member.Slot == null || !PlayerSlots.IsSlot(member.Slot.Number) || count >= order.Length)
            {
                continue;
            }

            int at = count++;
            while (at > 0 && order[at - 1].Slot.Number > member.Slot.Number)
            {
                order[at] = order[at - 1];
                at--;
            }

            order[at] = member;
        }

        ShownCount = Mathf.Min(count, view.Count);
        for (int i = 0; i < view.Count; i++)
        {
            PartyFrameView frame = view.Frame(i);
            if (frame == null)
            {
                continue;
            }

            if (i >= ShownCount)
            {
                frame.Hide();
                continue;
            }

            Show(frame, order[i], localWorld);
        }

        Array.Clear(order, 0, order.Length);
    }

    /// <summary>
    /// Shows <paramref name="member"/> on <paramref name="frame"/>: why it has no bar, or its health and debuffs.
    /// </summary>
    private void Show(PartyFrameView frame, Member member, WorldDefinition localWorld)
    {
        frame.ShowSlot(member.Slot.Number);
        WorldDefinition world = playerWorlds.Get(member.Handle);
        if (member.Slot.IsJoining)
        {
            frame.ShowState(Joining);
        }
        else if (localWorld != null && world != null && world != localWorld)
        {
            frame.ShowState(InAnotherWorld);
        }
        else if (member.Dead)
        {
            int remaining = Mathf.CeilToInt(settings.RespawnDelaySeconds - (clock.Time - member.DiedAt));
            frame.ShowState(remaining >= 1 && remaining < countdown.Length ? countdown[remaining] : NoticePresenter.Respawning);
        }
        else
        {
            Health health = member.Handle.Health;
            frame.ShowHealth(health != null && health.MaxHealth > 0 ? (float)health.CurrentHealth / health.MaxHealth : 0f);
            frame.ShowDebuffs(pipColours, Debuffs(member.Statuses));
        }
    }

    /// <summary>
    /// Fills the pip colours with <paramref name="statuses"/>' distinct debuffs, in the order they landed, up to four,
    /// and returns how many.
    /// </summary>
    private int Debuffs(StatusEffects statuses)
    {
        int count = 0;
        for (int i = 0; statuses != null && i < statuses.Count && count < MaxPips; i++)
        {
            StatusEffectDefinition definition = statuses.GetSnapshot(i).Definition;
            if (definition == null || definition.Kind != StatusKind.Debuff || Array.IndexOf(pipDefinitions, definition, 0, count) >= 0)
            {
                continue;
            }

            pipDefinitions[count] = definition;
            pipColours[count] = definition.Color;
            count++;
        }

        Array.Clear(pipDefinitions, 0, pipDefinitions.Length);
        return count;
    }

    /// <summary>
    /// Starts following <paramref name="player"/>, its slot and its statuses.
    /// </summary>
    private void Add(PlayerHandle player)
    {
        if (player == null || player.Transform == null)
        {
            return;
        }

        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].Handle == player)
            {
                return;
            }
        }

        player.Transform.TryGetComponent(out PlayerSlot slot);
        player.Transform.TryGetComponent(out StatusEffects statuses);
        members.Add(new Member(player, slot, statuses));
    }

    /// <summary>
    /// Stops following <paramref name="player"/>.
    /// </summary>
    private void Remove(PlayerHandle player)
    {
        for (int i = members.Count - 1; i >= 0; i--)
        {
            if (members[i].Handle == player)
            {
                members.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Another player this presenter follows: its handle, slot and statuses, and when this machine saw it die.
    /// </summary>
    private sealed class Member
    {
        /// <summary>
        /// Follows <paramref name="handle"/> with its <paramref name="slot"/> and <paramref name="statuses"/>.
        /// </summary>
        public Member(PlayerHandle handle, PlayerSlot slot, StatusEffects statuses)
        {
            Handle = handle;
            Slot = slot;
            Statuses = statuses;
        }

        public PlayerHandle Handle { get; }
        public PlayerSlot Slot { get; }
        public StatusEffects Statuses { get; }
        public bool Dead { get; private set; }
        public float DiedAt { get; private set; }

        /// <summary>
        /// Records the time <paramref name="now"/> as it is first seen dead, and forgets it once it lives again.
        /// </summary>
        public void Track(float now)
        {
            bool dead = Handle.Health != null && Handle.Health.IsDead;
            if (dead && !Dead)
            {
                DiedAt = now;
            }

            Dead = dead;
        }
    }
}
