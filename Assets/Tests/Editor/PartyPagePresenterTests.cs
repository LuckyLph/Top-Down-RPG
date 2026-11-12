using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using UnityEngine.UI;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for the in-game menu's Party page (<see cref="PartyPagePresenter"/>, Docs/UiRevamp.md, Phase 6) on the real
/// canvas prefab's page, over players whose slots are set without the network: a row per player with a slot in slot
/// order, the local one tagged "You" and slot 1 "Host" elsewhere; "Joining", "In another world" and "Down" in place of
/// the bar; the host's addresses and port, a client's none; Copy putting "address:port" on the clipboard and saying
/// "Copied" for its time; and ticking allocating nothing.
/// </summary>
public class PartyPagePresenterTests
{
    private const string GameMenuCanvasPath = "Assets/Prefabs/UI/GameMenuCanvas.prefab";

    private readonly List<Object> created = new();
    private GameObject canvas;
    private PartyPageView page;
    private PlayerRegistry players;
    private LocalPlayerTracker localPlayer;
    private PlayerWorlds playerWorlds;
    private FakeSessionStatus session;
    private FakeNetworkInterfaces interfaces;
    private FakeClipboard clipboard;
    private ManualWallClock wallClock;
    private PartyPagePresenter presenter;

    /// <summary>
    /// Instantiates the menu canvas and starts a presenter on its Party page, online as a client.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        canvas = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(GameMenuCanvasPath)));
        page = canvas.GetComponentInChildren<PartyPageView>(true);
        players = new PlayerRegistry();
        localPlayer = new LocalPlayerTracker();
        playerWorlds = new PlayerWorlds(players);
        session = new FakeSessionStatus { IsActive = true };
        interfaces = new FakeNetworkInterfaces();
        clipboard = new FakeClipboard();
        wallClock = new ManualWallClock();
        presenter = new PartyPagePresenter(page, players, localPlayer, playerWorlds, new ManualClock(), session, new HostAddresses(interfaces, 7777), clipboard, wallClock, Track(HudSettings.Create(messageSeconds: 1.2f)));
        presenter.Start();
    }

    /// <summary>
    /// Disposes the presenter and destroys every object the test made.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        presenter.Dispose();
        foreach (Object instance in created)
        {
            if (instance != null)
            {
                Object.DestroyImmediate(instance);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// On a client, every player with a slot has a row in slot order, the local player's included and tagged "You",
    /// slot 1 (the host's own player) tagged "Host"; a player without a slot has none.
    /// </summary>
    [Test]
    public void OnAClient_EveryPlayerHasARow_InSlotOrder_YouAndTheHostTagged()
    {
        AddPlayer("Third", 3);
        AddPlayer("Local", 2, isLocal: true);
        AddPlayer("Host", 1);
        AddPlayer("Unslotted", 0);

        presenter.Open();

        Assert.That(presenter.ShownCount, Is.EqualTo(3));
        Assert.That(page.Row(0).ShownSlot, Is.EqualTo(1));
        Assert.That(page.Row(0).ShownTag, Is.EqualTo(PartyPagePresenter.Host));
        Assert.That(page.Row(1).ShownSlot, Is.EqualTo(2));
        Assert.That(page.Row(1).ShownTag, Is.EqualTo(PartyPagePresenter.You));
        Assert.That(page.Row(2).ShownSlot, Is.EqualTo(3));
        Assert.That(page.Row(2).ShownTag, Is.Empty);
        Assert.That(page.Row(3).IsShown, Is.False);
        Assert.That(page.IsInviteShown, Is.False, "Only the host invites.");
        Assert.That(page.FirstSelectable, Is.EqualTo((Selectable)page.BackButton));
    }

    /// <summary>
    /// On the host its own row, slot 1, is "You", not "Host".
    /// </summary>
    [Test]
    public void OnTheHost_ItsOwnRowIsYou()
    {
        session.IsHost = true;
        AddPlayer("Host", 1, isLocal: true);
        AddPlayer("Client", 2);

        presenter.Open();

        Assert.That(page.Row(0).ShownTag, Is.EqualTo(PartyPagePresenter.You));
        Assert.That(page.Row(1).ShownTag, Is.Empty);
    }

    /// <summary>
    /// A row says "Joining" while its player loads, "In another world" while it is in another world than the local
    /// player, "Down" while it is dead, and otherwise shows its health; it follows each change as the page ticks.
    /// </summary>
    [Test]
    public void ARow_SaysJoining_InAnotherWorld_OrDown_ElseShowsItsHealth()
    {
        WorldDefinition here = Track(WorldDefinition.Create("Here", new Vector2Int(0, 0), new Vector2Int(1, 1), new[] { Vector2Int.zero }));
        WorldDefinition there = Track(WorldDefinition.Create("There", new Vector2Int(2048, 0), new Vector2Int(1, 1), new[] { Vector2Int.zero }));
        PlayerHandle local = AddPlayer("Local", 1, isLocal: true);
        PlayerHandle other = AddPlayer("Other", 2, joining: true);
        playerWorlds.Set(local, here);
        playerWorlds.Set(other, here);
        presenter.Open();
        Assert.That(page.Row(1).ShownState, Is.EqualTo(PartyFramesPresenter.Joining));

        other.Transform.GetComponent<PlayerSlot>().Configure(2, false);
        playerWorlds.Set(other, there);
        presenter.Tick();
        Assert.That(page.Row(1).ShownState, Is.EqualTo(PartyFramesPresenter.InAnotherWorld));

        playerWorlds.Set(other, here);
        other.Health.ApplyDamage(other.Health.MaxHealth);
        presenter.Tick();
        Assert.That(page.Row(1).IsDownShown, Is.True);
        Assert.That(page.Row(1).ShownState, Is.Null);

        other.Health.Restore();
        other.Health.ApplyDamage(other.Health.MaxHealth / 4);
        presenter.Tick();
        Assert.That(page.Row(1).IsDownShown, Is.False);
        Assert.That(page.Row(1).ShownFraction, Is.EqualTo(0.75f).Within(0.01f));
        Assert.That(page.Row(0).ShownFraction, Is.EqualTo(1f), "The local player's own bar shows too.");
    }

    /// <summary>
    /// The host sees the addresses friends join at, each with its network's name, and the port; with none it says so.
    /// </summary>
    [Test]
    public void TheHost_SeesTheAddressesAndThePort()
    {
        session.IsHost = true;
        AddPlayer("Host", 1, isLocal: true);
        interfaces.Add("Wi-Fi", true, "192.168.1.20");
        interfaces.Add("Tailscale", false, "100.101.102.103");

        presenter.Open();

        Assert.That(page.IsInviteShown, Is.True);
        Assert.That(page.ShownAddresses, Is.EqualTo(2));
        Assert.That(page.Address(0), Is.EqualTo(("192.168.1.20", HostAddresses.HomeNetwork)));
        Assert.That(page.Address(1), Is.EqualTo(("100.101.102.103", HostAddresses.Tailscale)));
        Assert.That(page.FirstSelectable, Is.EqualTo((Selectable)page.CopyButton(0)));
        Assert.That(page.CopyButton(1).navigation.selectOnDown, Is.EqualTo((Selectable)page.BackButton));
        Assert.That(page.BackButton.navigation.selectOnDown, Is.EqualTo((Selectable)page.CopyButton(0)), "The page's controls loop.");

        presenter.Close();
        interfaces.Clear();
        presenter.Open();
        Assert.That(page.ShownAddresses, Is.Zero, "Read again as the page opens.");
        Assert.That(page.IsInviteShown, Is.True);
    }

    /// <summary>
    /// Copy puts "address:port" on the clipboard and says "Copied" on its button for the message time of real time.
    /// </summary>
    [Test]
    public void Copy_PutsTheAddressAndPortOnTheClipboard_AndSaysCopiedForItsTime()
    {
        session.IsHost = true;
        AddPlayer("Host", 1, isLocal: true);
        interfaces.Add("Wi-Fi", true, "192.168.1.20");
        presenter.Open();

        page.CopyButton(0).onClick.Invoke();
        Assert.That(clipboard.Text, Is.EqualTo("192.168.1.20:7777"));
        Assert.That(page.CopyLabel(0), Is.EqualTo(PartyPagePresenter.CopiedLabel));

        wallClock.Advance(1.1);
        presenter.Tick();
        Assert.That(page.CopyLabel(0), Is.EqualTo(PartyPagePresenter.CopiedLabel));

        wallClock.Advance(0.2);
        presenter.Tick();
        Assert.That(page.CopyLabel(0), Is.EqualTo(PartyPagePresenter.CopyLabel));
    }

    /// <summary>
    /// Ticking the open page with a bar, a downed player and a joining one allocates no managed memory.
    /// </summary>
    [Test]
    public void Ticking_AllocatesNothing()
    {
        AddPlayer("Local", 1, isLocal: true);
        PlayerHandle down = AddPlayer("Down", 2);
        AddPlayer("Joining", 3, joining: true);
        down.Health.ApplyDamage(down.Health.MaxHealth);
        presenter.Open();
        presenter.Tick();

        Assert.That(() => presenter.Tick(), Is.Not.AllocatingGCMemory());
        Assert.That(presenter.ShownCount, Is.EqualTo(3));
    }

    /// <summary>
    /// Creates and registers a player of 100 HP with statuses and a slot of <paramref name="slot"/>, joining per
    /// <paramref name="joining"/>; with <paramref name="isLocal"/>, as the local player; returns its registry handle.
    /// </summary>
    private PlayerHandle AddPlayer(string name, int slot, bool joining = false, bool isLocal = false)
    {
        GameObject unit = Track(new GameObject(name));
        PlayerController controller = unit.AddComponent<PlayerController>();
        Health health = unit.AddComponent<Health>();
        SerializedObject healthSettings = new(health);
        healthSettings.FindProperty("maxHealth").intValue = 100;
        healthSettings.ApplyModifiedPropertiesWithoutUndo();
        unit.AddComponent<StatusEffects>();
        unit.AddComponent<PlayerSlot>().Configure(slot, joining);
        LocalPlayer asLocal = isLocal ? new LocalPlayer(controller) : null;
        PlayerHandle handle = asLocal != null ? asLocal.Handle : new PlayerHandle(unit.transform, health);
        players.Add(handle);
        if (asLocal != null)
        {
            localPlayer.Assign(asLocal);
        }

        return handle;
    }

    /// <summary>
    /// Records <paramref name="instance"/> for destruction on teardown and returns it.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
