using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for players' looks (Docs/AsepriteCharacters.md, Phase 2): a catalog finds its looks by index and by name; a
/// player's look shows a catalog look on its visuals and ignores an index the catalog lacks; on the owner it follows
/// the local settings' choice, the first look for an empty or unknown name or no settings, raising each look it shows,
/// until it stops following; and the local settings keep the look's name across a restart, telling only of a change.
/// </summary>
public class CharacterLookTests
{
    private readonly List<Object> created = new();
    private CharacterLocomotionSettings locomotion;
    private CharacterLocomotionSettings banditLocomotion;
    private CharacterAnimationSet rogueSet;
    private CharacterAnimationSet banditSet;
    private CharacterLookCatalog catalog;

    /// <summary>
    /// Builds a catalog of two looks, a rogue and a bandit, each with a set of its own.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        locomotion = Track(CharacterLocomotionSettings.Create(0.1f, 2.4f, 5.6f, 0.1f, 0.5f, 1.5f, 8f));
        banditLocomotion = Track(CharacterLocomotionSettings.Create(0.1f, 2f, 5f, 0.1f, 0.5f, 1.5f, 8f));
        rogueSet = Track(CharacterAnimationSet.Create(new CharacterAnimationClip[0]));
        banditSet = Track(CharacterAnimationSet.Create(new CharacterAnimationClip[0]));
        catalog = Track(CharacterLookCatalog.Create(new CharacterLook("Rogue", rogueSet, locomotion), new CharacterLook("Bandit", banditSet, banditLocomotion)));
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
    /// A catalog gives its looks by index and the index of a look by name, nothing out of range or for an unknown or
    /// empty name.
    /// </summary>
    [Test]
    public void ACatalog_FindsItsLooksByIndexAndName()
    {
        Assert.That(catalog.Count, Is.EqualTo(2));
        Assert.That(catalog.Get(1).Name, Is.EqualTo("Bandit"));
        Assert.That(catalog.Get(1).AnimationSet, Is.EqualTo(banditSet));
        Assert.That(catalog.Get(1).Locomotion, Is.EqualTo(banditLocomotion));
        Assert.That(catalog.Get(-1), Is.Null);
        Assert.That(catalog.Get(2), Is.Null);
        Assert.That(catalog.IndexOf("Bandit"), Is.EqualTo(1));
        Assert.That(catalog.IndexOf("Rogue"), Is.EqualTo(0));
        Assert.That(catalog.IndexOf("Knight"), Is.EqualTo(-1));
        Assert.That(catalog.IndexOf(""), Is.EqualTo(-1));
        Assert.That(catalog.IndexOf(null), Is.EqualTo(-1));
    }

    /// <summary>
    /// Showing a look puts its set and locomotion settings on the visuals and remembers its index; an index the catalog
    /// lacks changes nothing.
    /// </summary>
    [Test]
    public void Show_PutsTheLookOnTheVisuals()
    {
        PlayerLook look = CreatePlayer(out CharacterVisuals visuals);

        Assert.That(look.Index, Is.EqualTo(-1));
        Assert.That(look.Show(1), Is.True);
        Assert.That(look.Index, Is.EqualTo(1));
        Assert.That(visuals.AnimationSet, Is.EqualTo(banditSet));
        Assert.That(visuals.Locomotion, Is.EqualTo(banditLocomotion));
        Assert.That(look.Show(5), Is.False);
        Assert.That(look.Show(-1), Is.False);
        Assert.That(look.Index, Is.EqualTo(1));
        Assert.That(visuals.AnimationSet, Is.EqualTo(banditSet));
    }

    /// <summary>
    /// The owner shows the look its settings name and raises it, then follows each change to it; an unknown name shows
    /// the first look. Once it stops following, a change shows nothing.
    /// </summary>
    [Test]
    public void FollowLocalChoice_ShowsTheSettingsLook_AndEveryChangeToIt()
    {
        UserSettings settings = new(new MemorySaveStore());
        settings.SetCharacterLook("Bandit");
        PlayerLook look = CreatePlayer(out CharacterVisuals visuals);
        look.Construct(settings);
        List<int> chosen = new();
        look.Chosen += chosen.Add;

        look.FollowLocalChoice();
        Assert.That(chosen, Is.EqualTo(new[] { 1 }));
        Assert.That(visuals.AnimationSet, Is.EqualTo(banditSet));

        settings.SetCharacterLook("Rogue");
        Assert.That(chosen, Is.EqualTo(new[] { 1, 0 }));
        Assert.That(visuals.AnimationSet, Is.EqualTo(rogueSet));

        settings.SetCharacterLook("Knight");
        Assert.That(chosen, Is.EqualTo(new[] { 1, 0, 0 }), "An unknown look shows the first.");

        settings.SetCharacterLook("Bandit");
        look.StopFollowing();
        settings.SetCharacterLook("Rogue");
        Assert.That(visuals.AnimationSet, Is.EqualTo(banditSet), "Stopped, it no longer follows.");
        Assert.That(chosen, Is.EqualTo(new[] { 1, 0, 0, 1 }));
    }

    /// <summary>
    /// Without settings (a player built without injection) the owner shows the first look.
    /// </summary>
    [Test]
    public void FollowLocalChoice_WithoutSettings_ShowsTheFirstLook()
    {
        PlayerLook look = CreatePlayer(out CharacterVisuals visuals);
        look.Show(1);

        look.FollowLocalChoice();

        Assert.That(look.Index, Is.EqualTo(0));
        Assert.That(visuals.AnimationSet, Is.EqualTo(rogueSet));
    }

    /// <summary>
    /// The settings keep the look's name in their file, which settings read again later find; setting the same name
    /// again neither writes nor tells of a change.
    /// </summary>
    [Test]
    public void TheSettings_KeepTheLooksName()
    {
        MemorySaveStore store = new();
        UserSettings settings = new(store);
        int changes = 0;
        settings.CharacterLookChanged += () => changes++;
        Assert.That(settings.CharacterLook, Is.Empty);

        Assert.That(settings.SetCharacterLook("Bandit"), Is.True);
        Assert.That(settings.SetCharacterLook("Bandit"), Is.False);
        Assert.That(changes, Is.EqualTo(1));
        Assert.That(new UserSettings(store).CharacterLook, Is.EqualTo("Bandit"));
    }

    /// <summary>
    /// A player with visuals on a child, drawn with the rogue's set, and a look over the catalog.
    /// </summary>
    private PlayerLook CreatePlayer(out CharacterVisuals visuals)
    {
        GameObject player = Track(new GameObject("Player"));
        GameObject child = new("Visuals");
        child.transform.SetParent(player.transform, false);
        child.AddComponent<SpriteRenderer>();
        visuals = child.AddComponent<CharacterVisuals>();
        visuals.SetLook(rogueSet, locomotion);
        PlayerLook look = player.AddComponent<PlayerLook>();
        look.Configure(catalog, visuals);
        return look;
    }

    /// <summary>
    /// Remembers <paramref name="instance"/> for destruction after the test.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
