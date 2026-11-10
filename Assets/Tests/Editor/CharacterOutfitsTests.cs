using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for players' outfits (Docs/PlayerCharacter.md, Phases 6 and 7) on the project's adventurer parts: the look's
/// own parts give the look's own set at once with nothing composed; other picks compose an outfit once, shared by every
/// wearer of the same picks (picks of an option the parts lack counting as the first), destroyed when the last wearer
/// lets go without spares, or else kept as a spare worn again at once, the oldest spare past the most kept destroyed;
/// the outfit keeps every clip of the look in order with its timing, contact frame and lights, over sprites cut from
/// one texture with its palette map, and names the dyes of the parts it wears.
/// </summary>
public class CharacterOutfitsTests
{
    private const string PartsPath = "Assets/Sprites/Characters/Adventurer/Parts/Adventurer_Parts.asset";

    private CharacterParts parts;
    private CharacterOutfits outfits;

    /// <summary>
    /// Loads the project's adventurer parts and makes an empty set of outfits.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        parts = AssetDatabase.LoadAssetAtPath<CharacterParts>(PartsPath);
        Assert.That(parts, Is.Not.Null);
        outfits = new CharacterOutfits(2, 700);
    }

    /// <summary>
    /// Destroys every outfit made.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        outfits.Dispose();
    }

    /// <summary>
    /// The look's own parts give the look's set at once, composing nothing.
    /// </summary>
    [Test]
    public void TheLooksOwnParts_GiveItsSetAtOnce()
    {
        List<CharacterAnimationSet> ready = new();
        CharacterOutfitLease lease = outfits.Wear(parts, default, ready.Add);

        Assert.That(ready, Is.EqualTo(new[] { parts.Look }));
        Assert.That(lease.Set, Is.EqualTo(parts.Look));
        Assert.That(outfits.Count, Is.Zero);
        lease.Release();
    }

    /// <summary>
    /// A hood and a cape compose an outfit once, which a second wearer of the same picks shares (one with an option the
    /// parts lack in another slot too, which counts as the first) and which lives until the last of them lets go. It
    /// keeps every clip of the look in order with its timing, contact frame and lights, over sprites cut from one
    /// texture with the palette map as their secondary texture, and names the hood's and the cape's dyes. Without spares
    /// kept, it goes with the last wearer.
    /// </summary>
    [Test]
    public void AnOutfit_IsComposedOnce_SharedByItsWearers_AndDestroyedWithTheLast()
    {
        outfits.Dispose();
        outfits = new CharacterOutfits(0, 700);
        CharacterPartPicks picks = default(CharacterPartPicks).With(CharacterSlot.Headwear, 1).With(CharacterSlot.Cape, 1);
        CharacterOutfitLease first = outfits.WearNow(parts, picks);
        CharacterOutfitLease second = outfits.WearNow(parts, picks.With(CharacterSlot.Legs, 77));
        CharacterAnimationSet set = first.Set;

        Assert.That(set, Is.Not.Null);
        Assert.That(second.Set, Is.EqualTo(set));
        Assert.That(outfits.Count, Is.EqualTo(1));
        Assert.That(set.Clips.Count, Is.EqualTo(parts.Look.Clips.Count));
        for (int c = 0; c < set.Clips.Count; c++)
        {
            CharacterAnimationClip clip = set.Clips[c];
            CharacterAnimationClip own = parts.Look.Clips[c];
            Assert.That(clip.Name, Is.EqualTo(own.Name));
            Assert.That(clip.IsComplete, Is.True, clip.Name);
            Assert.That(clip.FrameCount, Is.EqualTo(own.FrameCount), clip.Name);
            Assert.That(clip.Duration, Is.EqualTo(own.Duration), clip.Name);
            Assert.That(clip.ContactFrame, Is.EqualTo(own.ContactFrame), clip.Name);
            Assert.That(clip.GaitFrames, Is.EqualTo(own.GaitFrames), clip.Name);
            Assert.That(clip.HasLights, Is.EqualTo(own.HasLights), clip.Name);
        }

        Sprite sprite = set.Clips[0].SpriteAt(6, 0);
        Assert.That(sprite.texture, Is.Not.EqualTo(parts.Look.Clips[0].SpriteAt(6, 0).texture));
        Assert.That(sprite.GetSecondaryTextureCount(), Is.EqualTo(1));
        Assert.That(set.Carries(CharacterDye.Headwear), Is.True);
        Assert.That(set.Carries(CharacterDye.Cape), Is.True);
        Assert.That(set.Carries(CharacterDye.Skin), Is.True);

        first.Release();
        Assert.That(set != null, Is.True, "A wearer is left.");
        second.Release();
        second.Release();
        Assert.That(set == null, Is.True, "The last wearer let go.");
        Assert.That(outfits.Count, Is.Zero);
    }

    /// <summary>
    /// An outfit nobody wears any more is kept as a spare (Docs/PlayerCharacter.md, Phase 7), so wearing it again, as a
    /// player switching back to a weapon does, gives its set at once; past the two spares kept, the one let go longest
    /// ago is destroyed.
    /// </summary>
    [Test]
    public void OutfitsNobodyWears_AreKeptAsSpares_TheOldestPastThemDestroyed()
    {
        CharacterPartPicks greatsword = default(CharacterPartPicks).With(CharacterSlot.Weapon, 1);
        CharacterOutfitLease lease = outfits.WearNow(parts, greatsword);
        CharacterAnimationSet set = lease.Set;
        lease.Release();
        Assert.That(set != null, Is.True, "kept as a spare");
        Assert.That(outfits.SpareCount, Is.EqualTo(1));

        List<CharacterAnimationSet> ready = new();
        lease = outfits.Wear(parts, greatsword, ready.Add);
        Assert.That(ready, Is.EqualTo(new[] { set }), "worn again at once");
        Assert.That(outfits.SpareCount, Is.Zero);
        lease.Release();

        outfits.WearNow(parts, default(CharacterPartPicks).With(CharacterSlot.Weapon, 2)).Release();
        Assert.That(set != null, Is.True, "two spares are kept");
        outfits.WearNow(parts, default(CharacterPartPicks).With(CharacterSlot.Hands, 1)).Release();
        Assert.That(set == null, Is.True, "the oldest spare past two is destroyed");
        Assert.That(outfits.SpareCount, Is.EqualTo(2));
        Assert.That(outfits.Count, Is.EqualTo(2));
    }
}
