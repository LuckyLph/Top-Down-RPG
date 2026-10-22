using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for how the player and the Weasel prefabs are drawn and sorted (Docs/Animation.md, Rules), on the project's
/// prefabs and assets, which they only read: the player draws the rogue's baked set from its feet with no Mecanim
/// animator, and its weapon plays on those visuals; the sword's hit area is centred half a unit above the feet; the
/// sword's attack is in the set, which draws weapons only in attacks, the attack's 80 px frames lining up with the 64 px
/// locomotion frames at the feet, and the greatsword's and the longbow's too; at the player's base speed, and chilled, it
/// runs at an unclamped rate so its feet keep up; a sorter with a sorting point sorts by that point's height, and the
/// Weasel sorts by its feet.
/// </summary>
public class CharacterPrefabTests
{
    private const string PlayerPath = "Assets/Prefabs/Player/Player.prefab";
    private const string WeaselPath = "Assets/Prefabs/Mobs/Weasel.prefab";
    private const string SetPath = "Assets/Sprites/Characters/Rogue/Rogue_AnimationSet.asset";
    private const string ChillPath = "Assets/Data/StatusEffects/Status_Chill.asset";
    private const string SwordPath = "Assets/Data/Weapons/Sword.asset";
    private const string ArrowPath = "Assets/Prefabs/Combat/Arrow.prefab";
    private const string SwordArtPath = "Assets/Settings/Characters/Weapons/WeaponArt_Sword.asset";
    private const string WeaponsFolder = "Assets/Data/Weapons/";
    private const string WeaponArtFolder = "Assets/Settings/Characters/Weapons/";
    private const float OnePixel = 1f / 32f;

    private readonly List<Object> created = new();

    /// <summary>
    /// Destroys every object the test made.
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
    /// The player's <c>Visuals</c> child sits on its root, the feet, and draws the rogue's set through
    /// <see cref="CharacterVisuals"/> (which its motor feeds) with a sprite pivoted on the feet; nothing in the prefab
    /// is a Mecanim animator; its sorter sorts by the root, the feet, so a hit's nudge of the sprite never re-sorts it;
    /// the feet collider on the root starts within a pixel of the feet and lies within the sprite's width; and neither
    /// the player nor the arrow it shoots is scaled, so their pixels are the terrain's.
    /// </summary>
    [Test]
    public void ThePlayer_DrawsTheRogueFromItsFeet()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        CharacterAnimationSet set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(SetPath);
        Assert.That(player.GetComponentsInChildren<Animator>(true), Is.Empty);
        foreach (Transform part in player.GetComponentsInChildren<Transform>(true))
        {
            Assert.That(part.localScale, Is.EqualTo(Vector3.one), $"{part.name} is not scaled.");
        }

        foreach (Transform part in AssetDatabase.LoadAssetAtPath<GameObject>(ArrowPath).GetComponentsInChildren<Transform>(true))
        {
            Assert.That(part.localScale, Is.EqualTo(Vector3.one), $"The arrow's {part.name} is not scaled.");
        }

        Transform visuals = player.transform.Find("Visuals");
        Assert.That(visuals, Is.Not.Null);
        Assert.That(visuals.localPosition, Is.EqualTo(Vector3.zero));
        CharacterVisuals characterVisuals = visuals.GetComponent<CharacterVisuals>();
        Assert.That(characterVisuals, Is.Not.Null);
        Assert.That(characterVisuals.AnimationSet, Is.EqualTo(set));
        Assert.That(new SerializedObject(characterVisuals).FindProperty("locomotion").objectReferenceValue, Is.Not.Null);
        Assert.That(new SerializedObject(player.GetComponent<PlayerMotor2D>()).FindProperty("visuals").objectReferenceValue, Is.EqualTo(characterVisuals));
        Assert.That(new SerializedObject(player.GetComponent<PlayerWeaponController>()).FindProperty("visuals").objectReferenceValue, Is.EqualTo(characterVisuals));

        Sprite sprite = visuals.GetComponent<SpriteRenderer>().sprite;
        Assert.That(sprite, Is.EqualTo(set.Locomotion(CharacterGait.Idle).SpriteAt(CharacterDirections.South, 0)));
        Assert.That(sprite.pivot, Is.EqualTo(new Vector2(32f, 12f)));

        SerializedObject sorter = new(visuals.GetComponent<YPositionSorter>());
        Assert.That(sorter.FindProperty("sortingPoint").objectReferenceValue, Is.SameAs(player.transform));
        Assert.That(sorter.FindProperty("sortingOrderOffset").intValue, Is.Zero);

        BoxCollider2D feet = player.GetComponent<BoxCollider2D>();
        Assert.That(feet.offset.y - feet.size.y * 0.5f, Is.EqualTo(0f).Within(OnePixel));
        Assert.That(feet.size.x * 0.5f, Is.LessThanOrEqualTo(sprite.bounds.extents.x));
    }

    /// <summary>
    /// The sword's hit area keeps the old slash's place around the feet: each direction's offset is half a unit higher
    /// than it was against the old centred sprite.
    /// </summary>
    [Test]
    public void TheSwordsHitArea_StaysHalfAUnitAboveTheFeet()
    {
        PlayerWeapon sword = AssetDatabase.LoadAssetAtPath<PlayerWeapon>(SwordPath);
        Assert.That(sword.HitOffset(Vector2.down), Is.EqualTo(new Vector2(0f, 0.4f)));
        Assert.That(sword.HitOffset(Vector2.up), Is.EqualTo(new Vector2(0f, 0.5f)));
        Assert.That(sword.HitOffset(Vector2.left), Is.EqualTo(new Vector2(-0.1f, 0.5f)));
        Assert.That(sword.HitOffset(Vector2.right), Is.EqualTo(new Vector2(0.1f, 0.5f)));
    }

    /// <summary>
    /// The sword's attack is in the rogue's set with the contact frame its motion keys (frame 4, after a windup of frames
    /// 0 to 3), and the set holds no carried clips: the weapons only show in their attacks. The attack's 80 px
    /// frames pivot on the feet like the 64 px locomotion frames: in every direction the first attack frame and the first
    /// idle frame put the top of the hood at the same height above the feet and the boots within a pixel of each other
    /// (the sword's own pixels left out, since its first frame holds it low).
    /// </summary>
    [Test]
    public void TheSwordsClips_AreInTheSet_AndLineUpWithTheLocomotionAtTheFeet()
    {
        PlayerWeapon sword = AssetDatabase.LoadAssetAtPath<PlayerWeapon>(SwordPath);
        CharacterAnimationSet set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(SetPath);
        CharacterAnimationClip attack = set.Find(sword.AttackClip);
        CharacterAnimationClip idle = set.Locomotion(CharacterGait.Idle);
        Assert.That(attack, Is.Not.Null);
        Assert.That(attack.HasContact, Is.True);
        Assert.That(attack.ContactFrame, Is.EqualTo(4));
        foreach (CharacterAnimationClip clip in set.Clips)
        {
            Assert.That(clip.Armed, Is.EqualTo(clip.HasContact), $"{clip.Name}: only attacks draw a weapon.");
        }

        Texture2D attackSheet = Track(LoadSheet(attack.SpriteAt(0, 0)));
        Texture2D idleSheet = Track(LoadSheet(idle.SpriteAt(0, 0)));
        HashSet<Color32> swordColours = WeaponColours(AssetDatabase.LoadAssetAtPath<CharacterWeaponArt>(SwordArtPath));
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            Sprite attackFrame = attack.SpriteAt(direction, 0);
            Sprite idleFrame = idle.SpriteAt(direction, 0);
            Assert.That(attackFrame.rect.size, Is.EqualTo(new Vector2(80f, 80f)));
            Assert.That(idleFrame.rect.size, Is.EqualTo(new Vector2(64f, 64f)));
            OpaqueRows(attackSheet, attackFrame, swordColours, out int attackBottom, out int attackTop);
            OpaqueRows(idleSheet, idleFrame, swordColours, out int idleBottom, out int idleTop);
            string name = CharacterDirections.Name(direction);
            Assert.That(attackTop, Is.EqualTo(idleTop), $"{name}: the hood's top.");
            Assert.That(Mathf.Abs(attackBottom - idleBottom), Is.LessThanOrEqualTo(1), $"{name}: the boots.");
        }
    }

    /// <summary>
    /// The greatsword's and the longbow's attacks (contact frame 5 each) start where the character stands: in every
    /// direction their first frame (96 and 80 px, pivoted on the feet) puts the top of the hood within a pixel of the
    /// idle's and the boots within two, the weapon's own pixels left out. The greatsword's first pose bends its knees,
    /// sitting the hood a pixel lower in most directions.
    /// </summary>
    [Test]
    public void TheGreatswordAndBowAttacks_LineUpWithTheLocomotionAtTheFeet()
    {
        CharacterAnimationSet set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(SetPath);
        CharacterAnimationClip idle = set.Locomotion(CharacterGait.Idle);
        Texture2D idleSheet = Track(LoadSheet(idle.SpriteAt(0, 0)));
        string[] weapons = { "Greatsword", "Longbow" };
        string[] arts = { "WeaponArt_Greatsword", "WeaponArt_Bow" };
        float[] sizes = { 96f, 80f };
        for (int w = 0; w < weapons.Length; w++)
        {
            PlayerWeapon weapon = AssetDatabase.LoadAssetAtPath<PlayerWeapon>(WeaponsFolder + weapons[w] + ".asset");
            CharacterAnimationClip attack = set.Find(weapon.AttackClip);
            Assert.That(attack, Is.Not.Null, weapons[w]);
            Assert.That(attack.ContactFrame, Is.EqualTo(5), weapons[w]);
            Texture2D attackSheet = Track(LoadSheet(attack.SpriteAt(0, 0)));
            HashSet<Color32> weaponColours = WeaponColours(AssetDatabase.LoadAssetAtPath<CharacterWeaponArt>(WeaponArtFolder + arts[w] + ".asset"));
            for (int direction = 0; direction < CharacterDirections.Count; direction++)
            {
                Sprite attackFrame = attack.SpriteAt(direction, 0);
                Assert.That(attackFrame.rect.size, Is.EqualTo(new Vector2(sizes[w], sizes[w])));
                OpaqueRows(attackSheet, attackFrame, weaponColours, out int attackBottom, out int attackTop);
                OpaqueRows(idleSheet, idle.SpriteAt(direction, 0), weaponColours, out int idleBottom, out int idleTop);
                string name = $"{weapons[w]} {CharacterDirections.Name(direction)}";
                Assert.That(Mathf.Abs(attackTop - idleTop), Is.LessThanOrEqualTo(1), $"{name}: the hood's top.");
                Assert.That(Mathf.Abs(attackBottom - idleBottom), Is.LessThanOrEqualTo(2), $"{name}: the boots.");
            }
        }
    }

    /// <summary>
    /// At the player's base move speed, and slowed by Chill, the player's locomotion settings pick the rogue's run and
    /// play it at speed over its natural speed without clamping, so its feet keep up going east or west.
    /// </summary>
    [Test]
    public void ThePlayersSpeeds_Run_WithTheFeetKeepingUp()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        CharacterVisuals visuals = player.GetComponentInChildren<CharacterVisuals>(true);
        CharacterLocomotionSettings locomotion = (CharacterLocomotionSettings)new SerializedObject(visuals).FindProperty("locomotion").objectReferenceValue;
        float baseSpeed = player.GetComponent<PlayerController>().ControlSettings.MoveSpeed;
        float chill = AssetDatabase.LoadAssetAtPath<StatusEffectDefinition>(ChillPath).MoveSpeedMultiplier;
        CharacterAnimationClip run = visuals.AnimationSet.Locomotion(CharacterGait.Run);

        foreach (float speed in new[] { baseSpeed, baseSpeed * chill })
        {
            CharacterAnimator animator = new(visuals.AnimationSet, locomotion, new ManualClock());
            animator.Update(Vector2.right * speed, Vector2.right);
            Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run), $"{speed} u/s");
            Assert.That(animator.PlaybackRate, Is.EqualTo(speed / run.NaturalSpeed).Within(1e-5f), $"{speed} u/s");
            Assert.That(animator.PlaybackRate, Is.InRange(locomotion.MinRate, locomotion.MaxRate), $"{speed} u/s");
        }
    }

    /// <summary>
    /// A sorter with a sorting point orders its renderers by the point's height rather than its own, and the Weasel's
    /// sorts by its root, its feet, as the player's does.
    /// </summary>
    [Test]
    public void ASortingPoint_SortsByItsHeight_AndTheWeaselSortsByItsFeet()
    {
        GameObject unit = Track(new GameObject("Unit"));
        unit.transform.position = new Vector3(0f, 1f, 0f);
        GameObject sprite = new("Visuals");
        sprite.transform.SetParent(unit.transform, false);
        sprite.transform.localPosition = new Vector3(0f, 0.72f, 0f);
        SpriteRenderer renderer = sprite.AddComponent<SpriteRenderer>();
        YPositionSorter sorter = sprite.AddComponent<YPositionSorter>();
        sorter.SetSortingOrderOffset(0);
        Assert.That(renderer.sortingOrder, Is.EqualTo(-172));

        SerializedObject serialized = new(sorter);
        serialized.FindProperty("sortingPoint").objectReferenceValue = unit.transform;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        sorter.SetSortingOrderOffset(0);
        Assert.That(sorter.SortingY, Is.EqualTo(1f).Within(1e-5f));
        Assert.That(renderer.sortingOrder, Is.EqualTo(-100));

        GameObject weasel = AssetDatabase.LoadAssetAtPath<GameObject>(WeaselPath);
        YPositionSorter weaselSorter = weasel.transform.Find("Visuals").GetComponent<YPositionSorter>();
        Assert.That(new SerializedObject(weaselSorter).FindProperty("sortingPoint").objectReferenceValue, Is.EqualTo(weasel.transform));
    }

    /// <summary>
    /// A readable copy of the sheet <paramref name="sprite"/> is cut from, read from its PNG on disk.
    /// </summary>
    private static Texture2D LoadSheet(Sprite sprite)
    {
        Texture2D sheet = new(2, 2);
        sheet.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sprite.texture)));
        return sheet;
    }

    /// <summary>
    /// Every colour <paramref name="weapon"/>'s art draws its blade, guard, grip, smears and dust in.
    /// </summary>
    private static HashSet<Color32> WeaponColours(CharacterWeaponArt weapon)
    {
        HashSet<Color32> colours = new();
        foreach (CharacterMaterial material in System.Enum.GetValues(typeof(CharacterMaterial)))
        {
            if (!CharacterMaterials.IsWeapons(material))
            {
                continue;
            }

            ColourRamp ramp = weapon.Ramp(material);
            colours.Add(ramp.Outline);
            colours.Add(ramp.Shadow);
            colours.Add(ramp.Middle);
            colours.Add(ramp.Light);
        }

        return colours;
    }

    /// <summary>
    /// The lowest and highest rows of <paramref name="frame"/> with an opaque pixel of a colour not in
    /// <paramref name="ignored"/>, in pixels above its pivot.
    /// </summary>
    private static void OpaqueRows(Texture2D sheet, Sprite frame, HashSet<Color32> ignored, out int bottom, out int top)
    {
        Rect rect = frame.rect;
        bottom = int.MaxValue;
        top = int.MinValue;
        for (int y = 0; y < (int)rect.height; y++)
        {
            for (int x = 0; x < (int)rect.width; x++)
            {
                Color32 pixel = sheet.GetPixel((int)rect.x + x, (int)rect.y + y);
                if (pixel.a > 0 && !ignored.Contains(pixel))
                {
                    bottom = Mathf.Min(bottom, y);
                    top = Mathf.Max(top, y);
                }
            }
        }

        bottom -= Mathf.RoundToInt(frame.pivot.y);
        top -= Mathf.RoundToInt(frame.pivot.y);
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
