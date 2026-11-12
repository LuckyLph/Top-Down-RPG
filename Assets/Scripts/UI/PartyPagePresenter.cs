using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point for the in-game menu's Party page (Docs/UiRevamp.md, Phase 6), on this machine only. While
/// the page is open it shows a row per player with a slot, in slot order, the local player's included, every machine
/// listing the same players:
/// <list type="bullet">
/// <item>the slot's badge and label, tagged "You" for the local player and "Host" for slot 1, the host's own player,
/// on every other machine;</item>
/// <item>"Joining" while the player's machine is still loading, "In another world" while <see cref="PlayerWorlds"/> puts it
/// in another world than the local player, "Down" while it is dead, and otherwise its health.</item>
/// </list>
/// For the host it also shows the addresses friends join at (<see cref="HostAddresses"/>, read as the page opens) with
/// the port; a Copy button puts "address:port" on the clipboard and reads "Copied" for
/// <see cref="HudSettings.MessageSeconds"/> of real time. It polls health, which a client's copies change without events;
/// <see cref="Tick"/> allocates nothing.
/// </summary>
public sealed class PartyPagePresenter : IStartable, ITickable, IDisposable
{
    public const string You = "You";
    public const string Host = "Host";
    public const string CopyLabel = "Copy";
    public const string CopiedLabel = "Copied";
    public const string PortFormat = "port {0}";
    public const int HostSlot = 1;

    private readonly PartyPageView view;
    private readonly PartyRoster roster;
    private readonly LocalPlayerTracker localPlayer;
    private readonly PlayerWorlds playerWorlds;
    private readonly IClock clock;
    private readonly ISessionStatus session;
    private readonly HostAddresses hostAddresses;
    private readonly IClipboard clipboard;
    private readonly IWallClock wallClock;
    private readonly HudSettings settings;
    private readonly PartyMember[] order = new PartyMember[PlayerSlots.Count];
    private readonly List<HostAddress> addresses = new();
    private string portText = string.Empty;
    private int copied = -1;
    private DateTime copiedUntil;
    private bool started;

    /// <summary>
    /// Shows the players of <paramref name="players"/> and, for the host, <paramref name="hostAddresses"/> on
    /// <paramref name="view"/>, copying through <paramref name="clipboard"/>.
    /// </summary>
    public PartyPagePresenter(
        PartyPageView view,
        IPlayerRegistry players,
        LocalPlayerTracker localPlayer,
        PlayerWorlds playerWorlds,
        IClock clock,
        ISessionStatus session,
        HostAddresses hostAddresses,
        IClipboard clipboard,
        IWallClock wallClock,
        HudSettings settings)
    {
        this.view = view;
        roster = new PartyRoster(players);
        this.localPlayer = localPlayer;
        this.playerWorlds = playerWorlds;
        this.clock = clock;
        this.session = session;
        this.hostAddresses = hostAddresses;
        this.clipboard = clipboard;
        this.wallClock = wallClock;
        this.settings = settings;
    }

    public bool IsOpen { get; private set; }

    /// <summary>
    /// How many rows show, for tests.
    /// </summary>
    internal int ShownCount { get; private set; }

    /// <summary>
    /// Follows the registry, builds the port's text and wires the Copy buttons.
    /// </summary>
    public void Start()
    {
        if (started || view == null)
        {
            return;
        }

        started = true;
        roster.Start();
        portText = string.Format(PortFormat, hostAddresses.Port);
        for (int i = 0; i < view.AddressCapacity; i++)
        {
            int index = i;
            view.CopyButton(i).onClick.AddListener(() => Copy(index));
        }
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
        IsOpen = false;
        roster.Dispose();
    }

    /// <summary>
    /// Fills the page as it opens: the invite block for the host (the addresses read now) and the rows.
    /// </summary>
    public void Open()
    {
        if (!started)
        {
            return;
        }

        IsOpen = true;
        copied = -1;
        if (session.IsActive && session.IsHost)
        {
            hostAddresses.Read(addresses);
            view.ShowInvite(addresses, portText, CopyLabel);
        }
        else
        {
            addresses.Clear();
            view.HideInvite();
        }

        Refresh();
    }

    /// <summary>
    /// Stops filling the page.
    /// </summary>
    public void Close()
    {
        IsOpen = false;
        ResetCopied();
    }

    /// <summary>
    /// While the page is open, shows the rows as they are now and puts a Copy button's label back once its time is up.
    /// </summary>
    public void Tick()
    {
        if (!IsOpen)
        {
            return;
        }

        Refresh();
        if (copied >= 0 && wallClock.UtcNow >= copiedUntil)
        {
            ResetCopied();
        }
    }

    /// <summary>
    /// Puts "address:port" of the address at <paramref name="index"/> on the clipboard and shows "Copied" on its button.
    /// </summary>
    internal void Copy(int index)
    {
        if (!IsOpen || index < 0 || index >= addresses.Count)
        {
            return;
        }

        ResetCopied();
        clipboard.Copy($"{addresses[index].Address}:{hostAddresses.Port}");
        view.SetCopyLabel(index, CopiedLabel);
        copied = index;
        copiedUntil = wallClock.UtcNow + TimeSpan.FromSeconds(settings.MessageSeconds);
    }

    /// <summary>
    /// Shows a row per player with a slot, in slot order, and hides the rest.
    /// </summary>
    private void Refresh()
    {
        PlayerHandle local = localPlayer.Current?.Handle;
        Transform localTransform = local?.Transform;
        WorldDefinition localWorld = local != null ? playerWorlds.Get(local) : null;
        int count = Mathf.Min(roster.Order(clock.Time, null, order), view.RowCount);
        ShownCount = count;
        view.SetRowCount(count);
        for (int i = 0; i < count; i++)
        {
            PartyMember member = order[i];
            PartyRowView row = view.Row(i);
            bool isLocal = localTransform != null && member.Handle.Transform == localTransform;
            row.ShowPlayer(member.Slot.Number, isLocal ? You : member.Slot.Number == HostSlot ? Host : null);
            WorldDefinition world = playerWorlds.Get(member.Handle);
            if (member.Slot.IsJoining)
            {
                row.ShowState(PartyFramesPresenter.Joining);
            }
            else if (!isLocal && localWorld != null && world != null && world != localWorld)
            {
                row.ShowState(PartyFramesPresenter.InAnotherWorld);
            }
            else if (member.Dead)
            {
                row.ShowDown();
            }
            else
            {
                row.ShowHealth(member.HealthFraction);
            }
        }

        Array.Clear(order, 0, order.Length);
    }

    /// <summary>
    /// Puts the copied button's label back.
    /// </summary>
    private void ResetCopied()
    {
        if (copied >= 0)
        {
            view.SetCopyLabel(copied, CopyLabel);
            copied = -1;
        }
    }
}
