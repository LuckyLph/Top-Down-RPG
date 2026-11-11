using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using UnityEngine.UI;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for how <see cref="AbilityBarPresenter"/> answers a press (Docs/UiRevamp.md, Phase 1), on a bar built in code
/// over a real player whose orders a fake command source drives: the message line's words for each failure outcome, or
/// none, for the message time; the frame of the slot whose cast runs; the brackets of the slot whose cast waits in the
/// buffer, dropped without a word; and the hatch of each slot the player's crowd control blocks.
/// </summary>
public class AbilityBarPresenterTests
{
    private const float MessageSeconds = 1.2f;
    private const int CastSlot = 0;
    private const int BufferedSlot = 1;
    private const int PlainSlot = 2;
    private const int MovingSlot = 3;
    private const int EmptySlot = 5;

    private readonly List<Object> created = new();
    private ManualClock clock;
    private LocalPlayerTracker localPlayer;
    private AbilityBarView bar;
    private AbilitySlotView[] slots;
    private TextMeshProUGUI message;
    private AbilityBarPresenter presenter;
    private FakeCommands commands;

    /// <summary>
    /// Builds the bar and starts a presenter on it with a manual clock, fake key labels and a message time of
    /// <see cref="MessageSeconds"/>.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        clock = new ManualClock();
        clock.Advance(10f);
        localPlayer = new LocalPlayerTracker();
        commands = new FakeCommands();
        CreateBar();
        presenter = new AbilityBarPresenter(localPlayer, bar, clock, new FakeInputBindingLabels(), Track(HudSettings.Create(MessageSeconds)));
        presenter.Start();
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
    /// Each failure flashes its slot; those the player can act on show their words on the message line, and an empty
    /// slot, a dead player, and a buffered cast that expired or was dropped show none.
    /// </summary>
    [TestCase(CastOutcome.OnCooldown, "On cooldown")]
    [TestCase(CastOutcome.CastInProgress, "Already casting")]
    [TestCase(CastOutcome.NoTarget, "No target")]
    [TestCase(CastOutcome.Stunned, "Stunned")]
    [TestCase(CastOutcome.Silenced, "Silenced")]
    [TestCase(CastOutcome.Rooted, "Rooted")]
    [TestCase(CastOutcome.EmptySlot, null)]
    [TestCase(CastOutcome.Dead, null)]
    [TestCase(CastOutcome.Expired, null)]
    [TestCase(CastOutcome.Cancelled, null)]
    public void AFailure_FlashesItsSlot_AndSaysWhyWhenThePlayerCanAct(CastOutcome outcome, string words)
    {
        PlayerController player = CreatePlayer();

        player.GetComponent<PlayerAbilities>().ReportFailure(PlainSlot, outcome);

        Assert.That(slots[PlainSlot].Flash.enabled, Is.True, "Every failure flashes, as before.");
        Assert.That(bar.IsMessageShown, Is.EqualTo(words != null));
        if (words != null)
        {
            Assert.That(message.text, Is.EqualTo(words));
        }
    }

    /// <summary>
    /// The message shows for the message time and then hides; a new failure shows its words and restarts the time.
    /// </summary>
    [Test]
    public void TheMessage_ShowsForTheMessageTime_AndANewFailureRestartsIt()
    {
        PlayerAbilities abilities = CreatePlayer().GetComponent<PlayerAbilities>();
        abilities.ReportFailure(PlainSlot, CastOutcome.OnCooldown);

        clock.Advance(MessageSeconds - 0.2f);
        presenter.Tick();
        Assert.That(bar.IsMessageShown, Is.True);

        abilities.ReportFailure(PlainSlot, CastOutcome.NoTarget);
        clock.Advance(0.5f);
        presenter.Tick();
        Assert.That(bar.IsMessageShown, Is.True, "A new failure restarts the time.");
        Assert.That(message.text, Is.EqualTo("No target"));

        clock.Advance(MessageSeconds);
        presenter.Tick();
        Assert.That(bar.IsMessageShown, Is.False);
    }

    /// <summary>
    /// Through the player's orders: the slot whose cast runs is framed in Accent; a bufferable press during it puts the
    /// brackets on its slot, a plain one says "Already casting"; when the cast ends the buffered cast runs, framed, its
    /// brackets gone; and pressing the first slot again says "On cooldown".
    /// </summary>
    [Test]
    public void TheOrders_FrameTheCastingSlot_AndBracketTheBufferedOne()
    {
        PlayerController player = CreatePlayer();

        Press(player, CastSlot);
        Assert.That(slots[CastSlot].IsCasting, Is.True);
        Assert.That(slots[CastSlot].Border.color, Is.EqualTo(TestThemes.Shared.Colour(UiColour.Accent)));
        Assert.That(bar.IsMessageShown, Is.False);

        Press(player, BufferedSlot);
        Assert.That(slots[BufferedSlot].IsQueued, Is.True);
        Assert.That(slots[BufferedSlot].Queued.enabled, Is.True);

        Press(player, PlainSlot);
        Assert.That(message.text, Is.EqualTo("Already casting"));
        Assert.That(slots[PlainSlot].Flash.enabled, Is.True);

        clock.Advance(0.3f);
        Press(player, -1);
        Assert.That(slots[CastSlot].IsCasting, Is.False);
        Assert.That(slots[CastSlot].Border.color, Is.EqualTo(TestThemes.Shared.Colour(UiColour.Border)));
        Assert.That(slots[BufferedSlot].IsQueued, Is.False);
        Assert.That(slots[BufferedSlot].IsCasting, Is.True, "The buffered cast runs.");

        clock.Advance(0.3f);
        Press(player, -1);
        Press(player, CastSlot);
        Assert.That(message.text, Is.EqualTo("On cooldown"));
    }

    /// <summary>
    /// A buffered cast dropped by the stop key loses its brackets without a word on the message line.
    /// </summary>
    [Test]
    public void ADroppedBufferedCast_LosesItsBrackets_WithoutAWord()
    {
        PlayerController player = CreatePlayer();
        Press(player, CastSlot);
        Press(player, BufferedSlot);
        Assert.That(slots[BufferedSlot].IsQueued, Is.True);

        commands.Next = new PlayerCommand(Vector2.right, default, false, false, stopPressed: true);
        player.Tick();

        Assert.That(slots[BufferedSlot].IsQueued, Is.False);
        Assert.That(bar.IsMessageShown, Is.False);
    }

    /// <summary>
    /// A stun or a silence hatches every slot with an ability, a root only the slot whose cast moves the caster; an
    /// empty slot is never hatched, and the hatch goes with the status.
    /// </summary>
    [TestCase(StatusControls.Stun, true)]
    [TestCase(StatusControls.Silence, true)]
    [TestCase(StatusControls.Root, false)]
    public void CrowdControl_HatchesTheSlotsItBlocks(StatusControls controls, bool blocksEverySlot)
    {
        PlayerController player = CreatePlayer();
        StatusEffects statuses = player.GetComponent<StatusEffects>();
        StatusEffectDefinition status = Track(StatusEffectDefinition.Create(controls.ToString(), StatusKind.Debuff, 2f, controls: controls));

        statuses.Set.Apply(status, null, 1f);

        Assert.That(slots[CastSlot].IsDisabled, Is.EqualTo(blocksEverySlot));
        Assert.That(slots[PlainSlot].IsDisabled, Is.EqualTo(blocksEverySlot));
        Assert.That(slots[MovingSlot].IsDisabled, Is.True, "An ability that moves the caster is blocked by every control.");
        Assert.That(slots[MovingSlot].Hatch.enabled, Is.True);
        Assert.That(slots[EmptySlot].IsDisabled, Is.False, "An empty slot is never hatched.");

        statuses.Set.Remove(status);

        foreach (AbilitySlotView slot in slots)
        {
            Assert.That(slot.IsDisabled, Is.False);
        }
    }

    /// <summary>
    /// A player bound while it casts and is stunned shows both at once; a newly bound player shows its own state, and
    /// the previous one's casts and statuses no longer reach the bar; with no player the bar empties and the message
    /// hides.
    /// </summary>
    [Test]
    public void ANewLocalPlayer_ShowsItsOwnState()
    {
        PlayerController first = CreatePlayer(assign: false);
        PlayerController second = CreatePlayer(assign: false);
        first.Tick();
        commands.Next = Command(CastSlot);
        first.Tick();
        commands.Next = default;
        first.GetComponent<StatusEffects>().Set.Apply(Track(StatusEffectDefinition.Create("Stun", StatusKind.Debuff, 2f, controls: StatusControls.Stun)), null, 1f);

        localPlayer.Assign(new LocalPlayer(first));
        Assert.That(slots[CastSlot].IsCasting, Is.True);
        Assert.That(slots[PlainSlot].IsDisabled, Is.True);
        first.GetComponent<PlayerAbilities>().ReportFailure(PlainSlot, CastOutcome.Stunned);

        localPlayer.Assign(new LocalPlayer(second));
        Assert.That(slots[CastSlot].IsCasting, Is.False);
        Assert.That(slots[PlainSlot].IsDisabled, Is.False);
        Assert.That(bar.IsMessageShown, Is.False, "The message was the previous player's.");
        first.GetComponent<PlayerAbilities>().ReportFailure(PlainSlot, CastOutcome.OnCooldown);
        Assert.That(bar.IsMessageShown, Is.False);

        localPlayer.Clear(localPlayer.Current.Handle);
        Assert.That(slots[CastSlot].IsEmpty, Is.True);
    }

    /// <summary>
    /// Ticking the bar while a message shows, a cooldown runs and a flash fades allocates no managed memory.
    /// </summary>
    [Test]
    public void Tick_DoesNotAllocate()
    {
        PlayerController player = CreatePlayer();
        Press(player, CastSlot);
        player.GetComponent<PlayerAbilities>().ReportFailure(PlainSlot, CastOutcome.CastInProgress);
        presenter.Tick();

        TestDelegate tick = () =>
        {
            clock.Advance(0.01f);
            presenter.Tick();
        };

        Assert.That(tick, Is.Not.AllocatingGCMemory());
    }

    /// <summary>
    /// Builds a bar of one slot per ability slot, each with its fill, icon, name, cooldown, key, flash, frame, brackets
    /// and hatch, and the message line, all on the test theme.
    /// </summary>
    private void CreateBar()
    {
        GameObject barObject = Track(new GameObject("AbilityBar"));
        slots = new AbilitySlotView[AbilitySlots.Count];
        for (int i = 0; i < slots.Length; i++)
        {
            GameObject slotObject = new($"Slot{i + 1}");
            slotObject.transform.SetParent(barObject.transform);
            Image fill = slotObject.AddComponent<Image>();
            slots[i] = slotObject.AddComponent<AbilitySlotView>();
            slots[i].ConfigureReferences(
                fill,
                Child<Image>(slotObject, "Icon"),
                Child<TextMeshProUGUI>(slotObject, "Name"),
                Child<Image>(slotObject, "CooldownFill"),
                Child<TextMeshProUGUI>(slotObject, "CooldownText"),
                Child<TextMeshProUGUI>(slotObject, "Key"),
                Child<Image>(slotObject, "Flash"),
                Child<Image>(slotObject, "Frame"),
                Child<Image>(slotObject, "Queued"),
                Child<Image>(slotObject, "Hatch"));
            slots[i].ConfigureTheme(TestThemes.Shared);
        }

        message = Child<TextMeshProUGUI>(barObject, "MessageLine");
        bar = barObject.AddComponent<AbilityBarView>();
        bar.ConfigureReferences(slots, message);
    }

    /// <summary>
    /// Creates a player whose orders read the test's commands: a 0.3 s cast in <see cref="CastSlot"/>, a bufferable
    /// one in <see cref="BufferedSlot"/>, a plain one in <see cref="PlainSlot"/>, one whose cast moves the caster in
    /// <see cref="MovingSlot"/>, and a weapon with an ability in its first slot only; and makes it the local player
    /// unless <paramref name="assign"/> is false.
    /// </summary>
    private PlayerController CreatePlayer(bool assign = true)
    {
        GameObject player = Track(new GameObject("Player"));
        player.AddComponent<BoxCollider2D>();
        player.AddComponent<StatusEffects>();
        PlayerController controller = player.AddComponent<PlayerController>();
        player.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        controller.Construct(clock);
        TestPlayerControlSettings.Assign(controller, Track(TestPlayerControlSettings.Create()));
        controller.SetCommandSource(commands);
        PlayerWeaponController weapon = player.AddComponent<PlayerWeaponController>();
        weapon.Construct(TestCombat.CreateAttackService(FixedGameAuthority.Authoritative), null, clock, null);
        weapon.Equip(Track(PlayerWeapon.Create("Sword", 1, 0.35f, 0.55f, new Vector2[4], weaponAbilities: new[] { Ability("Whirl", 0f), null })));
        PlayerAbilities abilities = player.AddComponent<PlayerAbilities>();
        abilities.Construct(clock, TestCombat.CreateAbilityService(FixedGameAuthority.Authoritative));
        abilities.Configure(Track(PlayerClass.Create(
            "Test",
            Ability("Bolt", 0.3f),
            Ability("Leap", 0.2f, bufferable: true),
            Ability("Mend", 0f),
            Ability("Charge", 0.2f, CastMovement.Ability))));
        if (assign)
        {
            localPlayer.Assign(new LocalPlayer(controller));
        }

        return controller;
    }

    /// <summary>
    /// Ticks <paramref name="player"/> once with <paramref name="slot"/> pressed (none for -1), then clears the press.
    /// </summary>
    private void Press(PlayerController player, int slot)
    {
        commands.Next = Command(slot);
        player.Tick();
        commands.Next = default;
    }

    /// <summary>
    /// A command pressing <paramref name="slot"/> (none for -1) with the cursor to the right of the player.
    /// </summary>
    private static PlayerCommand Command(int slot)
    {
        return new PlayerCommand(Vector2.right, default, movePressed: false, moveHeld: false, stopPressed: false, abilitySlot: slot);
    }

    /// <summary>
    /// A tracked ability with a 2 s cooldown and the given cast time, cast movement and bufferability.
    /// </summary>
    private AbilityDefinition Ability(string name, float castTime, CastMovement movement = CastMovement.Stop, bool bufferable = false)
    {
        return Track(AbilityDefinition.Create(name, cooldown: 2f, castTime: castTime, castMovement: movement, bufferable: bufferable));
    }

    /// <summary>
    /// Adds a child named <paramref name="name"/> under <paramref name="parent"/> with a <typeparamref name="T"/>.
    /// </summary>
    private static T Child<T>(GameObject parent, string name) where T : Component
    {
        GameObject child = new(name);
        child.transform.SetParent(parent.transform);
        return child.AddComponent<T>();
    }

    /// <summary>
    /// Keeps <paramref name="instance"/> for <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }

    /// <summary>
    /// A command source that hands out whatever command the test put in <see cref="Next"/>.
    /// </summary>
    private sealed class FakeCommands : IPlayerCommandSource
    {
        public PlayerCommand Next { get; set; }

        /// <summary>
        /// Returns <see cref="Next"/>.
        /// </summary>
        public PlayerCommand ReadCommand()
        {
            return Next;
        }
    }
}
