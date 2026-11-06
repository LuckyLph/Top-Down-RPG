using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for the weapon catalog and the project's weapons (Docs/Animation.md, Phase 4): the catalog's indices (the
/// weapons' ids on the wire), the project catalog's order and the player prefab's use of it, each weapon's clip, hit,
/// icon and abilities, each weapon's timeline under attack speed with the contact frame showing at contact, and the weapon
/// sprite recipes baking what their icons and arrow sheet hold. The project's assets are only read.
/// </summary>
public class WeaponCatalogTests
{
    private const string CatalogPath = "Assets/Data/Weapons/WeaponCatalog.asset";
    private const string PlayerPrefabPath = "Assets/Prefabs/Player/Player.prefab";
    private const string AnimationSetPath = "Assets/Sprites/Characters/Adventurer/Adventurer_AnimationSet.asset";

    private readonly List<Object> created = new();

    /// <summary>
    /// Destroys the test weapons and catalogs.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object made in created)
        {
            if (made != null)
            {
                Object.DestroyImmediate(made);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// A catalog names each weapon by its index, -1 for one it does not list, null for an index out of range, and its
    /// next weapon wraps round and starts at the first for a weapon it does not list.
    /// </summary>
    [Test]
    public void ACatalog_NamesWeaponsByIndex()
    {
        PlayerWeapon first = Track(PlayerWeapon.Create("First", 1, 0.5f, 0.5f, new Vector2[4]));
        PlayerWeapon second = Track(PlayerWeapon.Create("Second", 1, 0.5f, 0.5f, new Vector2[4]));
        PlayerWeapon outside = Track(PlayerWeapon.Create("Outside", 1, 0.5f, 0.5f, new Vector2[4]));
        WeaponCatalog catalog = Track(WeaponCatalog.Create(first, second));

        Assert.That(catalog.Count, Is.EqualTo(2));
        Assert.That(catalog.IndexOf(first), Is.Zero);
        Assert.That(catalog.IndexOf(second), Is.EqualTo(1));
        Assert.That(catalog.IndexOf(outside), Is.EqualTo(-1));
        Assert.That(catalog.IndexOf(null), Is.EqualTo(-1));
        Assert.That(catalog.Get(1), Is.SameAs(second));
        Assert.That(catalog.Get(2), Is.Null);
        Assert.That(catalog.Get(-1), Is.Null);
        Assert.That(catalog.Next(first), Is.SameAs(second));
        Assert.That(catalog.Next(second), Is.SameAs(first));
        Assert.That(catalog.Next(outside), Is.SameAs(first));
        Assert.That(Track(WeaponCatalog.Create()).Next(first), Is.Null);
    }

    /// <summary>
    /// The project's catalog lists the sword, the greatsword and the longbow at 0, 1 and 2 (their ids on the wire,
    /// which only ever grow at the end), and the player prefab names weapons by it, starting with the sword.
    /// </summary>
    [Test]
    public void TheProjectCatalog_ListsTheThreeWeaponsInOrder_AndThePlayerUsesIt()
    {
        WeaponCatalog catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(CatalogPath);
        Assert.That(catalog, Is.Not.Null);
        Assert.That(catalog.Count, Is.EqualTo(3));
        Assert.That(catalog.Get(0).name, Is.EqualTo("Sword"));
        Assert.That(catalog.Get(1).name, Is.EqualTo("Greatsword"));
        Assert.That(catalog.Get(2).name, Is.EqualTo("Longbow"));

        PlayerWeaponController weapons = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponent<PlayerWeaponController>();
        Assert.That(weapons.Catalog, Is.SameAs(catalog));
        SerializedObject serialized = new(weapons);
        Assert.That(serialized.FindProperty("startingWeapon").objectReferenceValue, Is.SameAs(catalog.Get(0)));
    }

    /// <summary>
    /// Every catalog weapon plays a clip of the adventurer's set (the player's default look) that has a contact frame
    /// and draws a weapon, has its own generated HUD icon and two abilities no other weapon has, and a timeline that
    /// fills its cooldown at most. The greatsword is melee with a wider and deeper area than the sword; the longbow fires
    /// a homing projectile that flies past its attack range, with its arrow's visual.
    /// </summary>
    [Test]
    public void EveryCatalogWeapon_HasItsClipIconAbilitiesAndHit()
    {
        WeaponCatalog catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(CatalogPath);
        CharacterAnimationSet set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(AnimationSetPath);
        HashSet<AbilityDefinition> abilities = new();
        HashSet<Sprite> icons = new();
        for (int i = 0; i < catalog.Count; i++)
        {
            PlayerWeapon weapon = catalog.Get(i);
            CharacterAnimationClip clip = set.Find(weapon.AttackClip);
            Assert.That(clip, Is.Not.Null, weapon.name);
            Assert.That(clip.HasContact && clip.Armed, Is.True, weapon.name);
            Assert.That(weapon.HudIcon, Is.Not.Null, weapon.name);
            Assert.That(AssetDatabase.GetAssetPath(weapon.HudIcon), Does.StartWith(WeaponSpriteWriter.Folder + "/Icon_"), weapon.name);
            Assert.That(icons.Add(weapon.HudIcon), Is.True, weapon.name);
            for (int slot = 0; slot < PlayerWeapon.AbilityCount; slot++)
            {
                Assert.That(weapon.GetAbility(slot), Is.Not.Null, weapon.name);
                Assert.That(abilities.Add(weapon.GetAbility(slot)), Is.True, $"{weapon.name}'s abilities are its own.");
            }

            Assert.That(weapon.Timeline.Duration, Is.LessThanOrEqualTo(weapon.AttackCooldown + 0.0001f), weapon.name);
        }

        PlayerWeapon sword = catalog.Get(0);
        PlayerWeapon greatsword = catalog.Get(1);
        PlayerWeapon longbow = catalog.Get(2);
        Assert.That(sword.FiresProjectile || greatsword.FiresProjectile, Is.False);
        Assert.That(greatsword.HitSize.x, Is.GreaterThan(sword.HitSize.x));
        Assert.That(greatsword.HitSize.y, Is.GreaterThan(sword.HitSize.y));
        Assert.That(greatsword.AttackClip, Is.EqualTo("GreatswordCleave"));
        Assert.That(longbow.AttackClip, Is.EqualTo("BowShot"));
        Assert.That(longbow.FiresProjectile, Is.True);
        ProjectileSpec arrow = longbow.Projectile;
        Assert.That(arrow.Homing, Is.True);
        Assert.That(arrow.Range, Is.GreaterThan(longbow.AttackRange), "An arrow flies past the range the bow stops chasing at.");
        Assert.That(longbow.AttackRange, Is.GreaterThan(greatsword.AttackRange));
        Assert.That(arrow.Visual, Is.Not.Null);
        SerializedObject visual = new(arrow.Visual);
        DirectionalSprites directions = (DirectionalSprites)visual.FindProperty("sprites").objectReferenceValue;
        Assert.That(directions.Count, Is.EqualTo(32));
        for (int d = 0; d < directions.Count; d++)
        {
            Assert.That(directions.Get(d), Is.Not.Null);
        }
    }

    /// <summary>
    /// For every catalog weapon, its timeline divided by attack speed keeps its shape, and the action layer playing its
    /// clip on that timeline shows the contact frame from exactly the end of the windup, at attack speed 1 and 1.5.
    /// </summary>
    [Test]
    public void EveryCatalogWeapon_ShowsItsContactFrameAtContact_UnderAttackSpeed()
    {
        WeaponCatalog catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(CatalogPath);
        CharacterAnimationSet set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(AnimationSetPath);
        foreach (float speed in new[] { 1f, 1.5f })
        {
            for (int i = 0; i < catalog.Count; i++)
            {
                PlayerWeapon weapon = catalog.Get(i);
                AttackTimeline natural = weapon.Timeline;
                AttackTimeline timeline = natural.At(speed);
                Assert.That(timeline.Windup, Is.EqualTo(natural.Windup / speed).Within(0.0001f));
                Assert.That(timeline.Recovery / timeline.Windup, Is.EqualTo(natural.Recovery / natural.Windup).Within(0.0001f));

                CharacterAnimationClip clip = set.Find(weapon.AttackClip);
                CharacterActionLayer layer = new();
                Assert.That(layer.Play(clip, CharacterDirections.South, timeline, 0f, false), Is.True);
                layer.Advance(timeline.Windup - 0.001f);
                Assert.That(layer.Phase, Is.EqualTo(AttackPhase.Windup), $"{weapon.name} at {speed}");
                Assert.That(layer.Frame, Is.LessThan(clip.ContactFrame));
                layer.Advance(timeline.Windup + 0.001f);
                Assert.That(layer.Frame, Is.EqualTo(clip.ContactFrame), $"{weapon.name} at {speed}");
                layer.Advance(timeline.Windup + timeline.Contact + 0.001f);
                Assert.That(layer.Phase, Is.EqualTo(AttackPhase.Recovery));
                Assert.That(layer.Frame, Is.GreaterThan(clip.ContactFrame));
            }
        }
    }

    /// <summary>
    /// Every weapon sprite recipe bakes the same pixels twice and exactly what its written icon (and arrow sheet)
    /// holds; each arrow frame keeps its drawing off the frame's edge with the tip on the frame's middle.
    /// </summary>
    [Test]
    public void TheWeaponSpriteRecipes_BakeWhatIsWritten()
    {
        string[] guids = AssetDatabase.FindAssets("t:" + nameof(WeaponSpriteRecipe));
        Assert.That(guids.Length, Is.EqualTo(3));
        foreach (string guid in guids)
        {
            WeaponSpriteRecipe recipe = AssetDatabase.LoadAssetAtPath<WeaponSpriteRecipe>(AssetDatabase.GUIDToAssetPath(guid));
            int size = recipe.IconSize;
            Color32[] icon = WeaponSpriteWriter.BakeIcon(recipe);
            Assert.That(WeaponSpriteWriter.BakeIcon(recipe), Is.EqualTo(icon), recipe.name);
            string iconPath = $"{WeaponSpriteWriter.Folder}/{recipe.IconName}.png";
            Assert.That(File.ReadAllBytes(iconPath), Is.EqualTo(TerrainMaterialWriter.EncodePng(size, size, icon)), iconPath);
            if (!recipe.HasArrow)
            {
                continue;
            }

            int frame = recipe.ArrowFrame;
            int width = frame * recipe.ArrowDirections;
            Color32[] arrow = WeaponSpriteWriter.BakeArrow(recipe);
            string arrowPath = $"{WeaponSpriteWriter.Folder}/{recipe.ArrowName}.png";
            Assert.That(File.ReadAllBytes(arrowPath), Is.EqualTo(TerrainMaterialWriter.EncodePng(width, frame, arrow)), arrowPath);
            for (int direction = 0; direction < recipe.ArrowDirections; direction++)
            {
                bool tip = false;
                bool atEdge = false;
                for (int y = 0; y < frame; y++)
                {
                    for (int x = 0; x < frame; x++)
                    {
                        bool filled = arrow[y * width + direction * frame + x].a > 0;
                        atEdge |= filled && (x == 0 || y == 0 || x == frame - 1 || y == frame - 1);
                        tip |= filled && Mathf.Abs(x + 0.5f - frame / 2f) <= 2f && Mathf.Abs(y + 0.5f - frame / 2f) <= 2f;
                    }
                }

                Assert.That(atEdge, Is.False, $"Arrow direction {direction} reaches its frame's edge.");
                Assert.That(tip, Is.True, $"Arrow direction {direction} has its tip on the frame's middle.");
            }
        }
    }

    /// <summary>
    /// Keeps <paramref name="made"/> for <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T made) where T : Object
    {
        created.Add(made);
        return made;
    }
}
