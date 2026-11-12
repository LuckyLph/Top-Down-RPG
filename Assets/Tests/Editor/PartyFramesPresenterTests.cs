using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using UnityEngine.UI;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for the player slots and the HUD's party frames (Docs/UiRevamp.md, Phase 5): <see cref="PlayerSlots"/>' lowest
/// free slot, roles and labels, and <see cref="PartyFramesPresenter"/> on frames built in code, over players whose
/// slots are set without the network, on a manual clock. Playing alone no frame shows; the other players show in slot
/// order, the local one left out, each badge in its slot's colour; a player without a slot shows none, and one who
/// leaves takes its frame with it. A frame says "Joining", "In another world", or counts a death down from the respawn
/// delay then says "Respawning...", and otherwise shows its player's health and distinct debuffs. Ticking allocates
/// nothing.
/// </summary>
public class PartyFramesPresenterTests
{
    private const float RespawnDelay = 3f;

    private readonly List<Object> created = new();
    private ManualClock clock;
    private PlayerRegistry players;
    private LocalPlayerTracker localPlayer;
    private PlayerWorlds playerWorlds;
    private PartyFramesView view;
    private PartyFrameView[] frames;
    private PartyFramesPresenter presenter;
    private Player local;

    /// <summary>
    /// Builds three frames and a local player in slot 1, and starts a presenter on them with a 3 s respawn delay.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        clock = new ManualClock();
        clock.Advance(10f);
        players = new PlayerRegistry();
        localPlayer = new LocalPlayerTracker();
        playerWorlds = new PlayerWorlds(players);
        view = CreateView(3);
        presenter = new PartyFramesPresenter(players, localPlayer, playerWorlds, clock, Track(GameplaySettings.Create(respawnDelay: RespawnDelay)), view);
        presenter.Start();
        local = AddPlayer("Local", 1, isLocal: true);
    }

    /// <summary>
    /// Disposes the presenter and destroys every object the test made.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        presenter?.Dispose();
        presenter = null;
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
    /// The lowest slot no player holds is given, numbers outside the slots ignored, and none once all four are taken;
    /// each slot has its own colour role, Player1 to Player4, and its label.
    /// </summary>
    [Test]
    public void TheLowestFreeSlot_IsGiven_AndEachSlotHasItsRoleAndLabel()
    {
        Assert.That(PlayerSlots.LowestFree(new int[0]), Is.EqualTo(1));
        Assert.That(PlayerSlots.LowestFree(new[] { 1, 2 }), Is.EqualTo(3));
        Assert.That(PlayerSlots.LowestFree(new[] { 1, 3 }), Is.EqualTo(2), "A leaver's slot is the next one given.");
        Assert.That(PlayerSlots.LowestFree(new[] { 0, 5, 2 }), Is.EqualTo(1));
        Assert.That(PlayerSlots.LowestFree(new[] { 4, 3, 2, 1 }), Is.Zero);
        Assert.That(PlayerSlots.Role(1), Is.EqualTo(UiColour.Player1));
        Assert.That(PlayerSlots.Role(4), Is.EqualTo(UiColour.Player4));
        Assert.That(PlayerSlots.Label(2), Is.EqualTo("Player 2"));
        Assert.That(PlayerSlots.Label(0), Is.Empty);
    }

    /// <summary>
    /// Playing alone, no frame shows.
    /// </summary>
    [Test]
    public void Alone_NoFrameShows()
    {
        presenter.Tick();

        Assert.That(presenter.ShownCount, Is.Zero);
        foreach (PartyFrameView frame in frames)
        {
            Assert.That(frame.IsShown, Is.False);
        }
    }

    /// <summary>
    /// The other players show top to bottom in slot order, the local one left out, each frame's badge in its slot's
    /// colour; a player without a slot shows no frame, and a player who leaves takes its frame with it.
    /// </summary>
    [Test]
    public void OtherPlayers_ShowInSlotOrder_WithoutTheLocalOne()
    {
        local.Slot.Configure(2, false);
        AddPlayer("Fourth", 4);
        Player first = AddPlayer("First", 1);
        AddPlayer("Unslotted", 0);

        presenter.Tick();

        Assert.That(presenter.ShownCount, Is.EqualTo(2));
        Assert.That(frames[0].ShownSlot, Is.EqualTo(1));
        Assert.That(frames[1].ShownSlot, Is.EqualTo(4));
        Assert.That(frames[2].IsShown, Is.False);
        Assert.That(frames[0].Badge.color, Is.EqualTo(TestThemes.Shared.Colour(UiColour.Player1)));
        Assert.That(frames[1].Badge.color, Is.EqualTo(TestThemes.Shared.Colour(UiColour.Player4)));

        players.Remove(first.Handle);
        presenter.Tick();
        Assert.That(presenter.ShownCount, Is.EqualTo(1));
        Assert.That(frames[0].ShownSlot, Is.EqualTo(4));
        Assert.That(frames[1].IsShown, Is.False, "A leaver takes its frame with it.");
    }

    /// <summary>
    /// A player still joining says so; one in another world than the local player says so; one in the same world shows
    /// its bar.
    /// </summary>
    [Test]
    public void AFrame_SaysJoining_OrInAnotherWorld_ElseShowsTheBar()
    {
        WorldDefinition here = Track(WorldDefinition.Create("Here", new Vector2Int(0, 0), new Vector2Int(1, 1), new[] { Vector2Int.zero }));
        WorldDefinition there = Track(WorldDefinition.Create("There", new Vector2Int(2048, 0), new Vector2Int(1, 1), new[] { Vector2Int.zero }));
        Player other = AddPlayer("Other", 2, joining: true);
        playerWorlds.Set(local.Handle, here);
        playerWorlds.Set(other.Handle, there);

        presenter.Tick();
        Assert.That(frames[0].ShownState, Is.EqualTo(PartyFramesPresenter.Joining));
        Assert.That(frames[0].State.text, Is.EqualTo("Joining"));
        Assert.That(frames[0].Fill.transform.parent.gameObject.activeSelf, Is.False, "The line takes the bar's place.");

        other.Slot.Configure(2, false);
        presenter.Tick();
        Assert.That(frames[0].ShownState, Is.EqualTo(PartyFramesPresenter.InAnotherWorld));

        playerWorlds.Set(other.Handle, here);
        presenter.Tick();
        Assert.That(frames[0].ShownState, Is.Null);
        Assert.That(frames[0].State.gameObject.activeSelf, Is.False);
        Assert.That(frames[0].Fill.transform.parent.gameObject.activeSelf, Is.True);
        Assert.That(frames[0].ShownFraction, Is.EqualTo(1f));
    }

    /// <summary>
    /// Another player's death counts down in whole seconds from the respawn delay since this machine saw it, then says
    /// "Respawning..." until it is restored, when its bar comes back.
    /// </summary>
    [Test]
    public void ADeath_CountsDown_ThenSaysRespawning_UntilTheBarComesBack()
    {
        Player other = AddPlayer("Other", 2);
        presenter.Tick();

        other.Health.ApplyDamage(other.Health.MaxHealth);
        presenter.Tick();
        Assert.That(frames[0].ShownState, Is.EqualTo("Respawning in 3"));
        Advance(1.1f);
        Assert.That(frames[0].ShownState, Is.EqualTo("Respawning in 2"));
        Advance(1f);
        Assert.That(frames[0].ShownState, Is.EqualTo("Respawning in 1"));
        Advance(1f);
        Assert.That(frames[0].ShownState, Is.EqualTo(NoticePresenter.Respawning));
        Advance(5f);
        Assert.That(frames[0].ShownState, Is.EqualTo(NoticePresenter.Respawning), "It waits for the restore.");

        other.Health.Restore();
        presenter.Tick();
        Assert.That(frames[0].ShownState, Is.Null);
        Assert.That(frames[0].ShownFraction, Is.EqualTo(1f));
    }

    /// <summary>
    /// The bar shows the player's health, and the pips its distinct debuffs in the order they landed, up to four, in
    /// their colours, buffs left out.
    /// </summary>
    [Test]
    public void TheBar_ShowsHealth_AndUpToFourDistinctDebuffs()
    {
        Player other = AddPlayer("Other", 2);
        other.Health.ApplyDamage(other.Health.MaxHealth / 4);
        StatusEffectDefinition[] debuffs = new StatusEffectDefinition[5];
        for (int i = 0; i < debuffs.Length; i++)
        {
            debuffs[i] = Track(StatusEffectDefinition.Create($"Debuff{i}", StatusKind.Debuff, 10f, color: new Color(0.1f * (i + 1), 0.2f, 0.3f)));
        }

        other.Statuses.Set.Apply(Track(StatusEffectDefinition.Create("Buff", StatusKind.Buff, 10f)), null, 1f);
        other.Statuses.Set.Apply(debuffs[0], null, 1f);
        other.Statuses.Set.Apply(debuffs[0], null, 1f);
        other.Statuses.Set.Apply(debuffs[1], null, 1f);

        presenter.Tick();
        Assert.That(frames[0].ShownFraction, Is.EqualTo(0.75f).Within(0.01f));
        Assert.That(frames[0].ShownPips, Is.EqualTo(2), "One pip per debuff, buffs left out.");
        Assert.That(frames[0].Pips[0].enabled, Is.True);
        Assert.That(frames[0].Pips[0].color, Is.EqualTo(debuffs[0].Color));
        Assert.That(frames[0].Pips[1].color, Is.EqualTo(debuffs[1].Color));
        Assert.That(frames[0].Pips[2].enabled, Is.False);

        for (int i = 2; i < debuffs.Length; i++)
        {
            other.Statuses.Set.Apply(debuffs[i], null, 1f);
        }

        presenter.Tick();
        Assert.That(frames[0].ShownPips, Is.EqualTo(4), "Four at most.");
        Assert.That(frames[0].Pips[3].color, Is.EqualTo(debuffs[3].Color));
    }

    /// <summary>
    /// Ticking with frames showing a bar, a countdown and pips allocates no managed memory.
    /// </summary>
    [Test]
    public void Ticking_AllocatesNothing()
    {
        Player hurt = AddPlayer("Hurt", 2);
        Player dead = AddPlayer("Dead", 3);
        hurt.Statuses.Set.Apply(Track(StatusEffectDefinition.Create("Debuff", StatusKind.Debuff, 10f)), null, 1f);
        dead.Health.ApplyDamage(dead.Health.MaxHealth);
        presenter.Tick();

        TestDelegate tick = () =>
        {
            clock.Advance(0.4f);
            presenter.Tick();
        };

        Assert.That(tick, Is.Not.AllocatingGCMemory());
        Assert.That(presenter.ShownCount, Is.EqualTo(2));
    }

    /// <summary>
    /// Advances the clock by <paramref name="seconds"/> and ticks.
    /// </summary>
    private void Advance(float seconds)
    {
        clock.Advance(seconds);
        presenter.Tick();
    }

    /// <summary>
    /// Creates and registers a player of 100 HP with statuses and a slot of <paramref name="slot"/>, joining per
    /// <paramref name="joining"/>; with <paramref name="isLocal"/>, registered as the local player is, through its own
    /// handle.
    /// </summary>
    private Player AddPlayer(string name, int slot, bool joining = false, bool isLocal = false)
    {
        GameObject unit = Track(new GameObject(name));
        PlayerController controller = unit.AddComponent<PlayerController>();
        Health health = unit.AddComponent<Health>();
        SerializedObject healthSettings = new(health);
        healthSettings.FindProperty("maxHealth").intValue = 100;
        healthSettings.ApplyModifiedPropertiesWithoutUndo();
        StatusEffects statuses = unit.AddComponent<StatusEffects>();
        PlayerSlot playerSlot = unit.AddComponent<PlayerSlot>();
        playerSlot.Configure(slot, joining);
        LocalPlayer asLocal = isLocal ? new LocalPlayer(controller) : null;
        Player player = new(controller, health, statuses, playerSlot, asLocal != null ? asLocal.Handle : new PlayerHandle(unit.transform, health));
        players.Add(player.Handle);
        if (asLocal != null)
        {
            localPlayer.Assign(asLocal);
        }

        return player;
    }

    /// <summary>
    /// Creates a tracked canvas holding a <see cref="PartyFramesView"/> with <paramref name="count"/> frames, each with
    /// its badge, number, name, state line, bar with its fill and four pips.
    /// </summary>
    private PartyFramesView CreateView(int count)
    {
        GameObject canvas = Track(new GameObject("Canvas", typeof(RectTransform), typeof(Canvas)));
        frames = new PartyFrameView[count];
        for (int i = 0; i < count; i++)
        {
            GameObject frame = new($"PartyFrame{i}", typeof(RectTransform));
            frame.transform.SetParent(canvas.transform, false);
            Image badge = Child<Image>(frame, "Badge");
            TMP_Text number = Child<TextMeshProUGUI>(badge.gameObject, "Number");
            TMP_Text playerName = Child<TextMeshProUGUI>(frame, "Name");
            TMP_Text state = Child<TextMeshProUGUI>(frame, "State");
            Image bar = Child<Image>(frame, "Bar");
            Image fill = Child<Image>(bar.gameObject, "Fill");
            fill.type = Image.Type.Filled;
            Image[] pips = new Image[4];
            for (int p = 0; p < pips.Length; p++)
            {
                pips[p] = Child<Image>(frame, $"Pip{p}");
                pips[p].enabled = false;
            }

            frames[i] = frame.AddComponent<PartyFrameView>();
            frames[i].ConfigureReferences(badge, number, playerName, state, bar.gameObject, fill, pips, TestThemes.Shared);
            frame.SetActive(false);
        }

        PartyFramesView frameList = canvas.AddComponent<PartyFramesView>();
        frameList.ConfigureReferences(frames);
        return frameList;
    }

    /// <summary>
    /// Adds a child of <paramref name="parent"/> named <paramref name="name"/> with a <typeparamref name="T"/>.
    /// </summary>
    private static T Child<T>(GameObject parent, string name) where T : Component
    {
        GameObject child = new(name, typeof(RectTransform));
        child.transform.SetParent(parent.transform, false);
        return child.AddComponent<T>();
    }

    /// <summary>
    /// Records <paramref name="instance"/> for destruction on teardown and returns it.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }

    /// <summary>
    /// A player built for a test: its controller, health, statuses, slot and registry handle.
    /// </summary>
    private sealed class Player
    {
        /// <summary>
        /// Holds a player's parts.
        /// </summary>
        public Player(PlayerController controller, Health health, StatusEffects statuses, PlayerSlot slot, PlayerHandle handle)
        {
            Controller = controller;
            Health = health;
            Statuses = statuses;
            Slot = slot;
            Handle = handle;
        }

        public PlayerController Controller { get; }
        public Health Health { get; }
        public StatusEffects Statuses { get; }
        public PlayerSlot Slot { get; }
        public PlayerHandle Handle { get; }
    }
}
