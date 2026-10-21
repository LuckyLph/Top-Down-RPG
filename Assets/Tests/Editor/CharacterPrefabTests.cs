using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for how the player and the Weasel prefabs are drawn and sorted (Docs/Animation.md, Rules), on the project's
/// prefabs and assets, which they only read: the player draws the rogue's baked set from its feet with no Mecanim
/// animator, its slash anchor on the feet and the sword's slash offsets raised to keep the slash where it was; at the
/// player's base speed, and chilled, it runs at an unclamped rate so its feet keep up; a sorter with a sorting point
/// sorts by that point's height, and the Weasel sorts by its feet.
/// </summary>
public class CharacterPrefabTests
{
    private const string PlayerPath = "Assets/Prefabs/Player/Player.prefab";
    private const string WeaselPath = "Assets/Prefabs/Mobs/Weasel.prefab";
    private const string SetPath = "Assets/Sprites/Characters/Rogue/Rogue_AnimationSet.asset";
    private const string ChillPath = "Assets/Data/StatusEffects/Status_Chill.asset";
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
    /// is a Mecanim animator; its sorter sorts by its own height, the feet; and the feet collider on the root starts
    /// within a pixel of the feet and lies within the sprite's width.
    /// </summary>
    [Test]
    public void ThePlayer_DrawsTheRogueFromItsFeet()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        CharacterAnimationSet set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(SetPath);
        Assert.That(player.GetComponentsInChildren<Animator>(true), Is.Empty);

        Transform visuals = player.transform.Find("Visuals");
        Assert.That(visuals, Is.Not.Null);
        Assert.That(visuals.localPosition, Is.EqualTo(Vector3.zero));
        CharacterVisuals characterVisuals = visuals.GetComponent<CharacterVisuals>();
        Assert.That(characterVisuals, Is.Not.Null);
        Assert.That(characterVisuals.AnimationSet, Is.EqualTo(set));
        Assert.That(new SerializedObject(characterVisuals).FindProperty("locomotion").objectReferenceValue, Is.Not.Null);
        Assert.That(new SerializedObject(player.GetComponent<PlayerMotor2D>()).FindProperty("visuals").objectReferenceValue, Is.EqualTo(characterVisuals));

        Sprite sprite = visuals.GetComponent<SpriteRenderer>().sprite;
        Assert.That(sprite, Is.EqualTo(set.Locomotion(CharacterGait.Idle).SpriteAt(CharacterDirections.South, 0)));
        Assert.That(sprite.pivot, Is.EqualTo(new Vector2(32f, 12f)));

        SerializedObject sorter = new(visuals.GetComponent<YPositionSorter>());
        Assert.That(sorter.FindProperty("sortingPoint").objectReferenceValue, Is.Null);
        Assert.That(sorter.FindProperty("sortingOrderOffset").intValue, Is.Zero);

        BoxCollider2D feet = player.GetComponent<BoxCollider2D>();
        Assert.That(feet.offset.y - feet.size.y * 0.5f, Is.EqualTo(0f).Within(OnePixel));
        Assert.That(feet.size.x * 0.5f, Is.LessThanOrEqualTo(sprite.bounds.extents.x));
    }

    /// <summary>
    /// The sword's slashes follow the owner's sprite, now on the feet: each direction's slash offset is half a unit
    /// higher than it was against the old sprite, centred half a unit up.
    /// </summary>
    [Test]
    public void TheSwordsSlash_StaysHalfAUnitAboveTheFeet()
    {
        PlayerWeapon sword = AssetDatabase.LoadAssetAtPath<PlayerWeapon>("Assets/Data/Weapons/Sword.asset");
        Assert.That(sword.GetSlashSpawnOffset(Vector2.down), Is.EqualTo(new Vector2(0f, 0.4f)));
        Assert.That(sword.GetSlashSpawnOffset(Vector2.up), Is.EqualTo(new Vector2(0f, 0.5f)));
        Assert.That(sword.GetSlashSpawnOffset(Vector2.left), Is.EqualTo(new Vector2(-0.1f, 0.5f)));
        Assert.That(sword.GetSlashSpawnOffset(Vector2.right), Is.EqualTo(new Vector2(0.1f, 0.5f)));
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
    /// Remembers <paramref name="instance"/> for destruction after the test.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
