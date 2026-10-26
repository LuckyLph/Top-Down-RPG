using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="PropFading"/> (Docs/PropGeneration.md, In play), with props built in the test and the viewer's
/// feet given directly: a prop standing in front of the viewer and covering enough of its body fades to the settings'
/// opacity over their fade time and back once the viewer leaves; one standing behind it, one covering too little, one
/// that never fades and one far away do not; with no viewer everything fades back; taking a chunk's props out puts their
/// opacity back, destroyed ones included; and without settings nothing fades.
/// </summary>
public class PropFadingTests
{
    private const float Alpha = 0.4f;
    private const float Seconds = 0.2f;
    private static readonly Vector2 Body = new(0.5f, 1.3f);

    private readonly List<Object> created = new();
    private PropFadeSettings settings;
    private PropFading fading;

    /// <summary>
    /// Makes the settings and the fading under test.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        settings = Track(PropFadeSettings.Create(Alpha, Seconds, Body, 0.25f));
        fading = new PropFading(new LocalPlayerTracker(), settings);
    }

    /// <summary>
    /// Destroys everything the test made.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
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
    /// A tree standing a cell below the viewer, its drawing over the viewer's body, fades to the settings' opacity over
    /// their fade time, half way after half of it, and back to opaque over the same time once the viewer walks away.
    /// </summary>
    [Test]
    public void ATreeInFrontOfTheViewer_FadesOverTheFadeTime_AndBackOnceItLeaves()
    {
        PropFade tree = Tree(new Vector2(10f, 10f));
        fading.Add(new[] { tree });
        Vector2 behind = new(10f, 11f);

        fading.Fade(true, behind, Seconds * 0.5f);
        Assert.That(tree.Visuals.color.a, Is.EqualTo((1f + Alpha) * 0.5f).Within(1e-4f));
        fading.Fade(true, behind, Seconds);
        Assert.That(tree.Visuals.color.a, Is.EqualTo(Alpha).Within(1e-4f));
        Assert.That(fading.Shown, Is.EqualTo(1));

        fading.Fade(true, new Vector2(20f, 11f), Seconds);
        Assert.That(tree.Visuals.color.a, Is.EqualTo(1f));
        Assert.That(fading.Shown, Is.Zero, "A prop back to opaque is no longer looked at.");
    }

    /// <summary>
    /// A tree standing above the viewer (drawn behind it), one whose drawing covers too little of its body, one that
    /// never fades and one far away all stay opaque.
    /// </summary>
    [Test]
    public void PropsBehindTheViewer_CoveringTooLittle_NotFading_OrFarAway_StayOpaque()
    {
        Vector2 feet = new(10f, 10f);
        PropFade behind = Tree(new Vector2(10f, 10.5f));
        PropFade aside = Tree(new Vector2(10f + Body.x * 0.5f - 0.05f + 1.25f, 9f));
        PropFade never = Prop(new Vector2(10f, 9f), default);
        PropFade far = Tree(new Vector2(40f, 9f));
        fading.Add(new[] { behind, aside, never, far });

        fading.Fade(true, feet, Seconds);

        foreach (PropFade prop in new[] { behind, aside, never, far })
        {
            Assert.That(prop.Visuals.color.a, Is.EqualTo(1f), prop.name);
        }

        Assert.That(fading.Shown, Is.Zero);
    }

    /// <summary>
    /// With no viewer, a faded tree fades back.
    /// </summary>
    [Test]
    public void WithNoViewer_AFadedTree_FadesBack()
    {
        PropFade tree = Tree(new Vector2(0f, 0f));
        fading.Add(new[] { tree });
        fading.Fade(true, new Vector2(0f, 1f), Seconds);

        fading.Fade(false, Vector2.zero, Seconds);

        Assert.That(tree.Visuals.color.a, Is.EqualTo(1f));
    }

    /// <summary>
    /// Taking a chunk's props out puts a faded one's opacity back and forgets it, and takes out one already destroyed
    /// without failing.
    /// </summary>
    [Test]
    public void Remove_PutsBackTheOpacity_AndTakesOutDestroyedProps()
    {
        PropFade tree = Tree(new Vector2(0f, 0f));
        PropFade gone = Tree(new Vector2(0f, 0.5f));
        fading.Add(new[] { tree, gone });
        fading.Fade(true, new Vector2(0f, 1f), Seconds);
        Assert.That(fading.Shown, Is.EqualTo(2));
        Object.DestroyImmediate(gone.gameObject);

        fading.Remove(new[] { tree, gone });

        Assert.That(tree.Visuals.color.a, Is.EqualTo(1f));
        Assert.That(fading.Shown, Is.Zero);
        fading.Fade(true, new Vector2(0f, 1f), Seconds);
        Assert.That(tree.Visuals.color.a, Is.EqualTo(1f), "A prop taken out no longer fades.");
    }

    /// <summary>
    /// Without settings, a tree in front of the viewer stays opaque.
    /// </summary>
    [Test]
    public void WithoutSettings_NothingFades()
    {
        PropFading unset = new(new LocalPlayerTracker(), null);
        PropFade tree = Tree(new Vector2(0f, 0f));
        unset.Add(new[] { tree });

        unset.Fade(true, new Vector2(0f, 1f), Seconds);

        Assert.That(tree.Visuals.color.a, Is.EqualTo(1f));
    }

    /// <summary>
    /// A tree standing at <paramref name="at"/>: a drawing 2.5 units wide and 3.5 tall over its root.
    /// </summary>
    private PropFade Tree(Vector2 at)
    {
        return Prop(at, new Rect(-1.25f, -0.2f, 2.5f, 3.7f));
    }

    /// <summary>
    /// A prop standing at <paramref name="at"/> whose drawing covers <paramref name="cover"/> around its root.
    /// </summary>
    private PropFade Prop(Vector2 at, Rect cover)
    {
        GameObject root = Track(new GameObject($"Prop at {at}"));
        root.transform.position = at;
        GameObject visuals = new("Visuals");
        visuals.transform.SetParent(root.transform, false);
        PropFade fade = root.AddComponent<PropFade>();
        fade.Configure(visuals.AddComponent<SpriteRenderer>(), cover);
        return fade;
    }

    /// <summary>
    /// Records <paramref name="instance"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
