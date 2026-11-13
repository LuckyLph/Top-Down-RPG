using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for <see cref="WorldCastBar"/> (Docs/UiRevamp.md, Phase 4) on a player built in the test, its casts run on a
/// <see cref="ManualClock"/>: the owner's cast fills the bar over its cast time from the left and hides it when it
/// completes or is cancelled, an instant cast never shows it, a copy's cast fills from when it arrives and stays full
/// until the owner's completion or cancel arrives or its hold runs out, the bar hides for a dead player, and filling
/// allocates nothing.
/// </summary>
public class WorldCastBarTests
{
    private const float ArtPixelsPerUnit = 32f;
    private const int SlowSlot = 0;
    private const int InstantSlot = 1;
    private const float CastTime = 0.5f;
    private const float Hold = 0.5f;

    private readonly List<Object> createdObjects = new();
    private ManualClock clock;
    private Health health;
    private PlayerAbilities abilities;
    private WorldCastBar bar;
    private SpriteRenderer frame;
    private SpriteRenderer fill;

    /// <summary>
    /// Builds a player with a 0.5 s cast in its first slot and an instant one in its second, and a cast bar under it
    /// whose sliced frame and fill use the skin's bar sprites, 22 x 3 and 20 x 1 art px.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        clock = new ManualClock();
        GameObject player = Track(new GameObject("Player"));
        health = player.AddComponent<Health>();
        abilities = player.AddComponent<PlayerAbilities>();
        abilities.Construct(clock, TestCombat.CreateAbilityService(FixedGameAuthority.Authoritative));
        abilities.Configure(Track(PlayerClass.Create(
            "Test",
            Track(AbilityDefinition.Create("Slow", 0f, CastTime)),
            Track(AbilityDefinition.Create("Instant", 0f)))));
        GameObject barObject = new("CastBar");
        barObject.transform.SetParent(player.transform);
        frame = Part(barObject, "Frame", UiSkinPiece.BarFrame, new Vector2(22f, 3f));
        fill = Part(barObject, "Fill", UiSkinPiece.BarFill, new Vector2(20f, 1f));
        bar = barObject.AddComponent<WorldCastBar>();
        bar.Configure(abilities, health, frame, fill, clock);
    }

    /// <summary>
    /// Destroys the player, and with it the bar, and the assets the test made.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object created in createdObjects)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        createdObjects.Clear();
    }

    /// <summary>
    /// Until a cast starts the bar is hidden.
    /// </summary>
    [Test]
    public void TheBar_IsHiddenUntilACast()
    {
        bar.Refresh();

        Assert.That(bar.IsShown, Is.False);
        Assert.That(frame.enabled, Is.False);
        Assert.That(fill.enabled, Is.False);
    }

    /// <summary>
    /// The owner's cast shows the bar empty, fills it from the left edge over the cast time, and hides it on the tick
    /// the cast completes.
    /// </summary>
    [Test]
    public void TheOwnersCast_FillsOverItsCastTime_AndHidesWhenItCompletes()
    {
        float fullWidth = fill.size.x;
        float leftEdge = fill.transform.localPosition.x - fullWidth * 0.5f;
        Assert.That(abilities.TryCast(SlowSlot, Aim()), Is.EqualTo(CastOutcome.Started));
        bar.Refresh();
        Assert.That(bar.IsShown, Is.True);
        Assert.That(fill.enabled, Is.True);
        Assert.That(bar.FillFraction, Is.Zero);

        clock.Advance(CastTime * 0.4f);
        bar.Refresh();
        Assert.That(bar.FillFraction, Is.EqualTo(0.4f).Within(1e-4f));
        Assert.That(fill.size.x, Is.EqualTo(fullWidth * 0.4f).Within(1e-4f));
        Assert.That(fill.transform.localPosition.x - fill.size.x * 0.5f, Is.EqualTo(leftEdge).Within(1e-4f), "The bar fills from its left edge.");

        clock.Advance(CastTime * 0.6f);
        bar.Refresh();
        Assert.That(bar.FillFraction, Is.EqualTo(1f).Within(1e-4f));

        abilities.CompleteCast(SlowSlot);
        Assert.That(bar.IsShown, Is.False, "The bar hides as the cast completes.");
        Assert.That(fill.enabled, Is.False);
    }

    /// <summary>
    /// A cancelled cast hides the bar at once.
    /// </summary>
    [Test]
    public void ACancelledCast_HidesTheBar()
    {
        abilities.TryCast(SlowSlot, Aim());
        clock.Advance(0.2f);
        bar.Refresh();
        Assert.That(bar.IsShown, Is.True);

        abilities.CancelCast(SlowSlot);

        Assert.That(bar.IsShown, Is.False);
    }

    /// <summary>
    /// An instant cast never shows the bar, on the owner or on a copy.
    /// </summary>
    [Test]
    public void AnInstantCast_NeverShowsTheBar()
    {
        Assert.That(abilities.TryCast(InstantSlot, Aim()), Is.EqualTo(CastOutcome.Started));
        bar.Refresh();
        Assert.That(bar.IsShown, Is.False);
        abilities.CompleteCast(InstantSlot);

        abilities.PlayRemoteCast(InstantSlot, Aim());
        bar.Refresh();
        Assert.That(bar.IsShown, Is.False);
    }

    /// <summary>
    /// On a copy, the owner's cast fills the bar from when it arrives, stays full past its cast time while the owner's
    /// completion is on its way, and hides when the completion arrives.
    /// </summary>
    [Test]
    public void ACopysCast_FillsFromItsArrival_AndStaysFullUntilTheOwnersCompletion()
    {
        clock.Advance(3f);
        abilities.PlayRemoteCast(SlowSlot, Aim());
        bar.Refresh();
        Assert.That(bar.IsShown, Is.True);
        Assert.That(bar.FillFraction, Is.Zero);

        clock.Advance(CastTime * 0.5f);
        bar.Refresh();
        Assert.That(bar.FillFraction, Is.EqualTo(0.5f).Within(1e-4f));

        clock.Advance(CastTime * 0.5f + Hold * 0.5f);
        bar.Refresh();
        Assert.That(bar.IsShown, Is.True, "Past the cast time the copy waits for the owner's completion.");
        Assert.That(bar.FillFraction, Is.EqualTo(1f));

        abilities.CompleteRemoteCast();
        Assert.That(bar.IsShown, Is.False);
    }

    /// <summary>
    /// On a copy, the owner's cancel hides the bar, and a cast whose completion never arrives hides once its hold past
    /// the cast time runs out.
    /// </summary>
    [Test]
    public void ACopysCast_HidesOnTheOwnersCancel_OrWhenItsHoldRunsOut()
    {
        abilities.PlayRemoteCast(SlowSlot, Aim());
        clock.Advance(0.1f);
        bar.Refresh();
        abilities.StopRemoteCast();
        Assert.That(bar.IsShown, Is.False);

        abilities.PlayRemoteCast(SlowSlot, Aim());
        clock.Advance(CastTime + Hold - 0.01f);
        bar.Refresh();
        Assert.That(bar.IsShown, Is.True);
        clock.Advance(0.02f);
        bar.Refresh();
        Assert.That(bar.IsShown, Is.False, "A completion that never arrives does not leave the bar full.");
    }

    /// <summary>
    /// A player who dies while casting loses the bar, and a dead player's casts never show it.
    /// </summary>
    [Test]
    public void ADeadPlayer_ShowsNoCastBar()
    {
        abilities.PlayRemoteCast(SlowSlot, Aim());
        bar.Refresh();
        Assert.That(bar.IsShown, Is.True);

        health.ApplyDamage(health.MaxHealth);
        bar.Refresh();
        Assert.That(bar.IsShown, Is.False);

        abilities.PlayRemoteCast(SlowSlot, Aim());
        bar.Refresh();
        Assert.That(bar.IsShown, Is.False);
    }

    /// <summary>
    /// Filling the bar while a cast runs allocates no managed memory.
    /// </summary>
    [Test]
    public void FillingTheBar_DoesNotAllocate()
    {
        abilities.TryCast(SlowSlot, Aim());
        bar.Refresh();

        TestDelegate refresh = () =>
        {
            clock.Advance(0.01f);
            bar.Refresh();
        };

        Assert.That(refresh, Is.Not.AllocatingGCMemory());
        Assert.That(bar.IsShown, Is.True);
    }

    /// <summary>
    /// An aim to the right with no target.
    /// </summary>
    private static CastAim Aim()
    {
        return new CastAim(Vector2.right, Vector2.right, default);
    }

    /// <summary>
    /// Creates a child sprite renderer of <paramref name="parent"/> drawing the skin's <paramref name="piece"/>
    /// sliced at <paramref name="artSize"/> art px.
    /// </summary>
    private static SpriteRenderer Part(GameObject parent, string name, UiSkinPiece piece, Vector2 artSize)
    {
        GameObject part = new(name);
        part.transform.SetParent(parent.transform);
        SpriteRenderer renderer = part.AddComponent<SpriteRenderer>();
        renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiSkinRecipe.PathOf(piece));
        renderer.drawMode = SpriteDrawMode.Sliced;
        renderer.size = artSize / ArtPixelsPerUnit;
        return renderer;
    }

    /// <summary>
    /// Records <paramref name="created"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T created) where T : Object
    {
        createdObjects.Add(created);
        return created;
    }
}
