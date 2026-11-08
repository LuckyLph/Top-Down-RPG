using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tests for how the player and the Weasel prefabs are drawn and sorted (Docs/Animation.md, Rules), on the project's
/// prefabs and assets, which they only read: the player draws the adventurer's baked set from its feet with no Mecanim
/// animator, and its weapon plays on those visuals; the sword's hit area is centred half a unit above the feet; the
/// sword's attack is in the set, which draws weapons only in attacks, the attack's 80 px frames lining up with the 64 px
/// locomotion frames at the feet, and the greatsword's and the longbow's too; at the player's base speed, and chilled, it
/// runs at an unclamped rate so its feet keep up, and on either side of every boundary between its gaits the gait it
/// keeps plays unclamped too; its set holds its five idle animations, which its settings wait for; it plays every ability's cast clip, over the run for its Continue
/// casts; its look comes from the project's catalog, whose first look is the adventurer it is drawn as, the rogue retired
/// from it, and whose every look holds every clip it plays, standing, walking and running clips included; the player and
/// the Weasel sort as a group at their feet, the Weasel's bar apart from it over every unit; the Weasels draw the
/// Weasel's baked set from their feet with no Mecanim animator, their config's attack clip in it.
/// </summary>
public class CharacterPrefabTests
{
    private const string PlayerPath = "Assets/Prefabs/Player/Player.prefab";
    private const string WeaselPath = "Assets/Prefabs/Mobs/Weasel.prefab";
    private const string SetPath = "Assets/Sprites/Characters/Adventurer/Adventurer_AnimationSet.asset";
    private const string LooksPath = "Assets/Data/Characters/CharacterLooks.asset";
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
    /// The player's look draws on its visuals from the project's catalog of looks, whose first, the adventurer, is the set
    /// and locomotion the prefab is drawn with, the rogue no longer one of them; every look of it holds a complete clip
    /// for every clip the player plays (its class's and weapons' casts and its weapons' attacks) and complete standing,
    /// walking and running clips, at 32 pixels per unit and pivoted on the feet, with locomotion settings.
    /// </summary>
    [Test]
    public void ThePlayersLooks_HoldEveryClipItPlays()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        CharacterLookCatalog looks = AssetDatabase.LoadAssetAtPath<CharacterLookCatalog>(LooksPath);
        PlayerLook look = player.GetComponent<PlayerLook>();
        CharacterVisuals visuals = player.GetComponentInChildren<CharacterVisuals>(true);
        Assert.That(look, Is.Not.Null);
        SerializedObject serialized = new(look);
        Assert.That(serialized.FindProperty("catalog").objectReferenceValue, Is.EqualTo(looks));
        Assert.That(serialized.FindProperty("visuals").objectReferenceValue, Is.EqualTo(visuals));
        Assert.That(looks.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(looks.Get(0).Name, Is.EqualTo("Adventurer"));
        Assert.That(looks.IndexOf("Rogue"), Is.EqualTo(-1), "The rogue is retired.");
        Assert.That(looks.Get(0).AnimationSet, Is.EqualTo(visuals.AnimationSet), "The first look is the prefab's own.");
        Assert.That(looks.Get(0).Locomotion, Is.EqualTo(visuals.Locomotion));

        IReadOnlyList<(string Clip, string Need)> needs = CharacterClipNeeds.Of(player);
        Assert.That(needs.Count, Is.EqualTo(13));
        for (int i = 0; i < looks.Count; i++)
        {
            CharacterLook characterLook = looks.Get(i);
            Assert.That(characterLook.AnimationSet, Is.Not.Null, characterLook.Name);
            Assert.That(characterLook.Locomotion, Is.Not.Null, characterLook.Name);
            Assert.That(looks.IndexOf(characterLook.Name), Is.EqualTo(i), $"{characterLook.Name}: names are unique.");
            foreach ((string clipName, string need) in needs)
            {
                CharacterAnimationClip clip = characterLook.AnimationSet.Find(clipName);
                Assert.That(clip, Is.Not.Null, $"{characterLook.Name} lacks {clipName} for {need}.");
                Assert.That(clip.IsComplete && clip.HasContact, Is.True, $"{characterLook.Name}: {clipName}");
            }

            foreach (CharacterGait gait in new[] { CharacterGait.Idle, CharacterGait.Walk, CharacterGait.Run })
            {
                CharacterAnimationClip clip = characterLook.AnimationSet.Locomotion(gait);
                Assert.That(clip != null && clip.IsComplete, Is.True, $"{characterLook.Name}: {gait}");
                Sprite sprite = clip.SpriteAt(CharacterDirections.South, 0);
                Assert.That(sprite.pixelsPerUnit, Is.EqualTo(32f), $"{characterLook.Name}: {gait}");
                Assert.That(sprite.pivot, Is.EqualTo(new Vector2(32f, 12f)), $"{characterLook.Name}: {gait}");
            }
        }
    }

    /// <summary>
    /// The player's <c>Visuals</c> child sits on its root, the feet, and draws the adventurer's set through
    /// <see cref="CharacterVisuals"/> (which its motor feeds) with a sprite pivoted on the feet; nothing in the prefab
    /// is a Mecanim animator; its sorter sorts by the root, the feet, so a hit's nudge of the sprite never re-sorts it;
    /// the feet collider on the root starts within a pixel of the feet and lies within the sprite's width; and neither
    /// the player nor the arrow it shoots is scaled, so their pixels are the terrain's.
    /// </summary>
    [Test]
    public void ThePlayer_DrawsTheAdventurerFromItsFeet()
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

        SortingGroup group = player.GetComponent<SortingGroup>();
        Assert.That(group, Is.Not.Null, "The player sorts as a group at its root, its feet.");
        Assert.That(group.sortingLayerName, Is.EqualTo("Entities"));
        Assert.That(group.sortingOrder, Is.Zero);

        BoxCollider2D feet = player.GetComponent<BoxCollider2D>();
        Assert.That(feet.offset.y - feet.size.y * 0.5f, Is.EqualTo(0f).Within(OnePixel));
        Assert.That(feet.size.x * 0.5f, Is.LessThanOrEqualTo(sprite.bounds.extents.x));
    }

    /// <summary>
    /// The player plays its casts on its visuals (<see cref="PlayerCastVisuals"/>, wired to its abilities and visuals),
    /// and every ability of its class and of every catalog weapon names a cast clip in the adventurer's set with a
    /// release frame, released 60 ms and followed through for 0.24 s (the instant Leap's flourish 0.3 s); the Continue
    /// casts (Smite and Whirl) also name their clip over the run, eight run frames under the same cast frames and
    /// release, and the others none; only Smite, the one cast over 0.5 s, channels.
    /// </summary>
    [Test]
    public void ThePlayer_PlaysEveryAbilitysCastClip()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        CharacterAnimationSet set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(SetPath);
        PlayerCastVisuals castVisuals = player.GetComponent<PlayerCastVisuals>();
        Assert.That(castVisuals, Is.Not.Null);
        SerializedObject serialized = new(castVisuals);
        Assert.That(serialized.FindProperty("abilities").objectReferenceValue, Is.EqualTo(player.GetComponent<PlayerAbilities>()));
        Assert.That(serialized.FindProperty("visuals").objectReferenceValue, Is.EqualTo(player.GetComponentInChildren<CharacterVisuals>(true)));

        List<AbilityDefinition> abilities = new();
        PlayerClass playerClass = player.GetComponent<PlayerAbilities>().Class;
        for (int slot = 0; slot < PlayerClass.AbilityCount; slot++)
        {
            abilities.Add(playerClass.GetAbility(slot));
        }

        WeaponCatalog catalog = player.GetComponent<PlayerWeaponController>().Catalog;
        for (int i = 0; i < catalog.Count; i++)
        {
            abilities.Add(catalog.Get(i).GetAbility(0));
            abilities.Add(catalog.Get(i).GetAbility(1));
        }

        Assert.That(abilities.Count, Is.EqualTo(10));
        foreach (AbilityDefinition ability in abilities)
        {
            CharacterAnimationClip clip = set.Find(ability.CastClip);
            Assert.That(clip, Is.Not.Null, ability.name);
            Assert.That(clip.HasContact, Is.True, ability.name);
            Assert.That(clip.GaitFrames, Is.Zero, ability.name);
            Assert.That(ability.CastTimeline.Windup, Is.EqualTo(ability.CastTime), ability.name);
            Assert.That(ability.CastTimeline.Contact, Is.EqualTo(0.06f).Within(1e-6f), ability.name);
            Assert.That(ability.CastTimeline.Recovery, Is.EqualTo(ability.CastTime > 0f ? 0.24f : 0.3f).Within(1e-6f), ability.name);
            Assert.That(ability.ChannelBeat > 0f, Is.EqualTo(ability.CastTime > 0.5f), ability.name);
            if (ability.CastMovement != CastMovement.Continue)
            {
                Assert.That(ability.MovingCastClip, Is.Null.Or.Empty, ability.name);
                continue;
            }

            CharacterAnimationClip moving = set.Find(ability.MovingCastClip);
            Assert.That(moving, Is.Not.Null, ability.name);
            Assert.That(moving.GaitFrames, Is.EqualTo(set.Locomotion(CharacterGait.Run).FrameCount), ability.name);
            Assert.That(moving.FrameCount, Is.EqualTo(clip.FrameCount), ability.name);
            Assert.That(moving.ContactFrame, Is.EqualTo(clip.ContactFrame), ability.name);
            Assert.That(moving.IsComplete, Is.True, ability.name);
        }
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
    /// The sword's attack is in the adventurer's set with the contact frame its motion keys (frame 4, after a windup of
    /// frames 0 to 3), and the set holds no carried clips: the weapons only show in their attacks and their own casts,
    /// never standing or moving. The attack's 80 px frames pivot on the feet like the 64 px locomotion frames: in every
    /// direction the first attack frame and the first idle frame put the top of the head and the boots within a pixel of
    /// each other above the feet (the sword's own pixels left out, since its first frame holds it low).
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
            Assert.That(!clip.Armed || clip.HasContact, Is.True, $"{clip.Name}: only attacks and weapon casts draw a weapon.");
            Assert.That(clip.Gait == CharacterGait.None || !clip.Armed, Is.True, $"{clip.Name}: the character stands and moves unarmed.");
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
            Assert.That(Mathf.Abs(attackTop - idleTop), Is.LessThanOrEqualTo(1), $"{name}: the head's top.");
            Assert.That(Mathf.Abs(attackBottom - idleBottom), Is.LessThanOrEqualTo(1), $"{name}: the boots.");
        }
    }

    /// <summary>
    /// The greatsword's and the longbow's attacks (contact frame 5 each) start where the character stands: in every
    /// direction their first frame (96 and 80 px, pivoted on the feet) puts the top of the head within a pixel of the
    /// idle's (the greatsword's within two, its first pose bending its knees) and the boots within two, the weapon's own
    /// pixels left out.
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
        int[] headDrops = { 2, 1 };
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
                Assert.That(Mathf.Abs(attackTop - idleTop), Is.LessThanOrEqualTo(headDrops[w]), $"{name}: the head's top.");
                Assert.That(Mathf.Abs(attackBottom - idleBottom), Is.LessThanOrEqualTo(2), $"{name}: the boots.");
            }
        }
    }

    /// <summary>
    /// At the player's base move speed, and slowed by Chill, the player's locomotion settings pick the adventurer's run and
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
    /// Around each boundary between the player's gaits, walk to run and run to sprint, the gait the hysteresis keeps
    /// plays at its speed over its natural speed without clamping: the lower gait just under the share above the
    /// boundary where it gives way, the upper one at the share below it where it still holds. So the feet never slide
    /// changing gaits (Docs/PlayerCharacter.md, Phase 2).
    /// </summary>
    [Test]
    public void ThePlayersGaits_PlayUnclamped_OnEitherSideOfEveryBoundary()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        CharacterVisuals visuals = player.GetComponentInChildren<CharacterVisuals>(true);
        CharacterLocomotionSettings locomotion = (CharacterLocomotionSettings)new SerializedObject(visuals).FindProperty("locomotion").objectReferenceValue;
        CharacterAnimationSet set = visuals.AnimationSet;
        CharacterGait[] gaits = { CharacterGait.Walk, CharacterGait.Run, CharacterGait.Sprint };
        for (int i = 0; i + 1 < gaits.Length; i++)
        {
            CharacterGait lower = gaits[i];
            CharacterGait upper = gaits[i + 1];
            float boundary = locomotion.From(upper);
            float up = boundary * (1f + locomotion.GaitHysteresis) - 0.001f;
            float down = boundary * (1f - locomotion.GaitHysteresis);
            Assert.That(CharacterLocomotionLayer.Pick(lower, up, locomotion), Is.EqualTo(lower), $"{lower} at {up} u/s");
            Assert.That(CharacterLocomotionLayer.Pick(upper, down, locomotion), Is.EqualTo(upper), $"{upper} at {down} u/s");
            Assert.That(up / set.Locomotion(lower).NaturalSpeed, Is.InRange(locomotion.MinRate, locomotion.MaxRate), $"{lower} at {up} u/s");
            Assert.That(down / set.Locomotion(upper).NaturalSpeed, Is.InRange(locomotion.MinRate, locomotion.MaxRate), $"{upper} at {down} u/s");
        }
    }

    /// <summary>
    /// The player's set holds its five idle animations, complete in every direction in 64 px frames on the feet,
    /// unarmed and without a contact frame; its locomotion settings wait a few seconds of standing before the first and
    /// longer between the next (Docs/PlayerCharacter.md, Phase 3).
    /// </summary>
    [Test]
    public void ThePlayersSet_HoldsItsIdleAnimations_AndItsSettingsWaitForThem()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        CharacterVisuals visuals = player.GetComponentInChildren<CharacterVisuals>(true);
        List<string> names = new();
        foreach (CharacterAnimationClip clip in visuals.AnimationSet.Clips)
        {
            if (!clip.IsIdleAnimation)
            {
                continue;
            }

            names.Add(clip.Name);
            Assert.That(clip.IsComplete, Is.True, clip.Name);
            Assert.That(clip.Armed, Is.False, clip.Name);
            Assert.That(clip.ContactFrame, Is.EqualTo(-1), clip.Name);
            Assert.That(clip.Duration, Is.InRange(1f, 2.5f), clip.Name);
            Sprite first = clip.SpriteAt(CharacterDirections.South, 0);
            Assert.That(first.rect.size, Is.EqualTo(new Vector2(64f, 64f)), clip.Name);
            Assert.That(first.pixelsPerUnit, Is.EqualTo(32f), clip.Name);
            Assert.That(first.pivot, Is.EqualTo(visuals.AnimationSet.Locomotion(CharacterGait.Idle).SpriteAt(CharacterDirections.South, 0).pivot), clip.Name);
        }

        Assert.That(names, Is.EqualTo(new[] { "IdleLookAround", "IdleStretch", "IdleScratch", "IdleTapFoot", "IdleYawn" }));
        CharacterLocomotionSettings locomotion = visuals.Locomotion;
        Assert.That(locomotion.IdleAnimationAfter, Is.InRange(3f, 20f));
        Assert.That(locomotion.IdleAnimationGapMin, Is.GreaterThanOrEqualTo(locomotion.IdleAnimationAfter));
        Assert.That(locomotion.IdleAnimationGapMax, Is.GreaterThan(locomotion.IdleAnimationGapMin));
    }

    /// <summary>
    /// The Weasel sorts as one group at its root, its feet, as the player does, at a constant order on the Entities layer;
    /// its health bar sorts apart, at the root of the sorting on the Overlay layer, so it draws over every unit: inside the
    /// body's group it would draw on the Entities layer.
    /// </summary>
    [Test]
    public void TheWeasel_SortsAsAGroupAtItsFeet_AndItsBarOverEveryUnit()
    {
        GameObject weasel = AssetDatabase.LoadAssetAtPath<GameObject>(WeaselPath);
        SortingGroup group = weasel.GetComponent<SortingGroup>();
        Assert.That(group, Is.Not.Null);
        Assert.That(group.sortingLayerName, Is.EqualTo("Entities"));
        Assert.That(group.sortingOrder, Is.Zero);
        Assert.That(group.sortAtRoot, Is.False);

        SortingGroup bar = weasel.GetComponentInChildren<WorldHealthBar>(true).GetComponent<SortingGroup>();
        Assert.That(bar, Is.Not.Null, "The bar sorts apart from the body.");
        Assert.That(bar.sortingLayerName, Is.EqualTo("Overlay"));
        Assert.That(bar.sortAtRoot, Is.True);
    }

    /// <summary>
    /// The Weasel and the stress test's Weasel draw the Weasel's baked set (Docs/Animation.md, Phase 7): no Mecanim
    /// animator anywhere in them; a <c>Visuals</c> child on the root, the feet, with <see cref="CharacterVisuals"/> on
    /// the Weasel's set and locomotion settings and a sprite pivoted on the feet; the motor feeding those visuals and
    /// the hit feedback holding through them; and each one's config naming an attack clip the set holds, with its
    /// contact frame, on the Weasel's timeline.
    /// </summary>
    [Test]
    public void TheWeasels_DrawTheBakedWeaselFromTheirFeet()
    {
        CharacterAnimationSet set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>("Assets/Sprites/Characters/Weasel/Weasel_AnimationSet.asset");
        CharacterLocomotionSettings locomotion = AssetDatabase.LoadAssetAtPath<CharacterLocomotionSettings>("Assets/Data/Characters/Locomotion_Weasel.asset");
        Assert.That(set, Is.Not.Null);
        Assert.That(locomotion, Is.Not.Null);
        foreach (string path in new[] { WeaselPath, "Assets/Dev/StressTest/Weasel_StressTest.prefab" })
        {
            GameObject weasel = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(weasel.GetComponentsInChildren<Animator>(true), Is.Empty, path);
            Transform visuals = weasel.transform.Find("Visuals");
            Assert.That(visuals.localPosition, Is.EqualTo(Vector3.zero), path);
            CharacterVisuals character = visuals.GetComponent<CharacterVisuals>();
            Assert.That(character, Is.Not.Null, path);
            SerializedObject serialized = new(character);
            Assert.That(serialized.FindProperty("animationSet").objectReferenceValue, Is.EqualTo(set), path);
            Assert.That(serialized.FindProperty("locomotion").objectReferenceValue, Is.EqualTo(locomotion), path);
            Assert.That(new SerializedObject(weasel.GetComponent<MobMotor2D>()).FindProperty("visuals").objectReferenceValue, Is.EqualTo(character), path);
            Assert.That(new SerializedObject(weasel.GetComponent<HitFeedback>()).FindProperty("characterVisuals").objectReferenceValue, Is.EqualTo(character), path);
            Sprite sprite = visuals.GetComponent<SpriteRenderer>().sprite;
            Assert.That(sprite, Is.EqualTo(set.Locomotion(CharacterGait.Idle).SpriteAt(CharacterDirections.South, 0)), path);
            Assert.That(sprite.pivot, Is.EqualTo(new Vector2(32f, 12f)), path);

            MobConfig config = weasel.GetComponent<MobController>().Config;
            CharacterAnimationClip attack = set.Find(config.AttackClip);
            Assert.That(attack, Is.Not.Null, path);
            Assert.That(attack.HasContact, Is.True, path);
            Assert.That(config.AttackTimeline.Windup, Is.EqualTo(0.45f).Within(1e-5f), path);
            Assert.That(config.AttackTimeline.Contact, Is.EqualTo(0.05f).Within(1e-5f), path);
            Assert.That(config.AttackTimeline.Recovery, Is.EqualTo(0.25f).Within(1e-5f), path);
            Assert.That(config.AttackArea, Is.EqualTo(new Vector2(1f, 0.75f)), path);
        }
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
