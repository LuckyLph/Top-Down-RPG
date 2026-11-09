using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for the adventurer's character recipe, the player's base body (Docs/PlayerCharacter.md, Phases 0 to 4), on the
/// project's asset, which they only read, and on copies made in memory: baking gives the same pixels every time; the
/// standing adventurer is its target height; its face shows both eyes, their glints, the brows and the mouth from the
/// front, one eye in profile and nothing from behind; its ears show beside the head; its locks grow the hair past the
/// head's ball from the front and in profile; the outfit's options (short sleeves, gloves, the satchel's strap, the
/// skirt) draw as the recipe sets them; every clip has a row per direction and fits its frames; the attacks and casts
/// keyed on the rogue's body fit its body; its gaits are its own, styled, while the rogue's stay plain: the bob's top and
/// bottom on frames, the head following it late, the torso stretching at its top and squashing at its bottom, the
/// hair's tips moving, and the hands rising at both ends of their swing; its idle animations start and end on the
/// idle's pose, the first frame the idle's to the pixel, without a contact frame; the yawn shuts the eyes and opens the
/// mouth; its attacks are its own, two a weapon, each ending with the weapon where the other starts, styled while the
/// rogue's stay plain; its casts are its own and styled, the whirl and the sweep spinning a whole turn and the roll
/// unarmed, while the rogue's stay plain; a spin turns the whole character; a cast over the run takes the cast's spin and
/// adds its style to the run's; and the project's sheets and animation set are what the recipe bakes.
/// </summary>
public class AdventurerRecipeTests
{
    private const string RecipePath = "Assets/Settings/Characters/Recipe_Adventurer.asset";
    private const string RoguePath = "Assets/Settings/Characters/Recipe_Rogue.asset";
    private const int East = 0;
    private const int North = 2;
    private const int South = 6;
    private static readonly string[] IdleAnimations = { "IdleLookAround", "IdleStretch", "IdleScratch", "IdleTapFoot", "IdleYawn" };
    private static readonly string[] Casts = { "CastBolt", "CastLeap", "CastMend", "CastSmite", "CastWhirl", "CastLunge", "CastSweep", "CastCharge", "CastVolley", "CastRoll" };
    private static readonly string[][] AttackPairs =
    {
        new[] { "SwordSlash", "SwordBackhand" },
        new[] { "GreatswordCleave", "GreatswordSweep" },
        new[] { "BowShot", "BowCantedShot" },
    };

    private readonly List<Object> created = new();
    private AdventurerRecipe recipe;
    private CharacterBaker baker;
    private CharacterClip idle;
    private CharacterClip walk;
    private Dictionary<string, CharacterSheet> sheets;

    /// <summary>
    /// Every clip of the recipe baked once, on first use.
    /// </summary>
    private Dictionary<string, CharacterSheet> Sheets => sheets ??= recipe.Clips.ToDictionary(clip => clip.Name, baker.Bake);

    /// <summary>
    /// Loads the project's adventurer recipe and its idle and walk.
    /// </summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        recipe = AssetDatabase.LoadAssetAtPath<AdventurerRecipe>(RecipePath);
        Assert.That(recipe, Is.Not.Null);
        baker = new CharacterBaker(recipe);
        idle = recipe.Clips.FirstOrDefault(clip => clip.Name == "Idle");
        walk = recipe.Clips.FirstOrDefault(clip => clip.Name == "Walk");
        Assert.That(idle, Is.Not.Null);
        Assert.That(walk, Is.Not.Null);
    }

    /// <summary>
    /// Destroys every copy the test made.
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
    /// A new baker of the same recipe gives the same pixels, for the idle and the walk.
    /// </summary>
    [Test]
    public void Bake_SameRecipe_GivesTheSamePixels()
    {
        CharacterBaker again = new(recipe);
        foreach (CharacterClip clip in new[] { idle, walk })
        {
            CharacterSheet first = baker.Bake(clip);
            CharacterSheet second = again.Bake(clip);
            Assert.That(second.Pixels.SequenceEqual(first.Pixels), Is.True, clip.Name);
            Assert.That(first.FramesAtEdge, Is.Empty, clip.Name);
        }
    }

    /// <summary>
    /// The scale makes the standing adventurer the recipe's target height, outline included.
    /// </summary>
    [Test]
    public void StandingAdventurer_IsItsTargetHeight()
    {
        Assert.That(recipe.TargetHeight, Is.EqualTo(40));
        Assert.That(baker.StandingHeight(), Is.InRange(recipe.TargetHeight - 1, recipe.TargetHeight + 1));
    }

    /// <summary>
    /// Facing south the face shows both eyes whole, each with its glint in one pixel, the brows and the mouth; in
    /// profile one eye shows; facing north the face is turned away and none of them is drawn.
    /// </summary>
    [Test]
    public void Face_ShowsItsFeaturesFromTheFront_OneEyeInProfile_AndNoneFromBehind()
    {
        int eye = recipe.EyeWidth * recipe.EyeHeight;
        CharacterCanvas front = baker.Render(walk, South, 0);
        Assert.That(front.Count(CharacterMaterial.Eye) + front.Count(CharacterMaterial.EyeGlint), Is.EqualTo(2 * eye));
        Assert.That(front.Count(CharacterMaterial.EyeGlint), Is.EqualTo(2));
        Assert.That(front.Count(CharacterMaterial.Brow), Is.Positive);
        Assert.That(front.Count(CharacterMaterial.Mouth), Is.EqualTo(Mathf.RoundToInt(recipe.Mouth.y)));

        CharacterCanvas profile = baker.Render(walk, East, 0);
        Assert.That(profile.Count(CharacterMaterial.Eye) + profile.Count(CharacterMaterial.EyeGlint), Is.EqualTo(eye));

        CharacterCanvas back = baker.Render(walk, North, 0);
        foreach (CharacterMaterial feature in new[] { CharacterMaterial.Eye, CharacterMaterial.EyeGlint, CharacterMaterial.Brow, CharacterMaterial.Mouth })
        {
            Assert.That(back.Count(feature), Is.Zero, feature.ToString());
        }
    }

    /// <summary>
    /// The ears show beside the head facing south, and the head's skin is no hair.
    /// </summary>
    [Test]
    public void TheEars_ShowBesideTheHead()
    {
        CharacterCanvas front = baker.Render(idle, South, 0);
        Assert.That(PartPixels(front, CharacterPart.LeftEar), Is.Positive);
        Assert.That(PartPixels(front, CharacterPart.RightEar), Is.Positive);
        Assert.That(front.Count(CharacterMaterial.Skin), Is.Positive);
        Assert.That(front.Count(CharacterMaterial.Hair), Is.Positive);
    }

    /// <summary>
    /// The locks grow the hair past the head's ball: with none, fewer pixels are hair from the front and in profile.
    /// From behind the default style's locks mostly stand within the ball's outline, so north is left out.
    /// </summary>
    [Test]
    public void TheLocks_GrowTheHairPastTheHeadsBall()
    {
        Assert.That(recipe.Locks, Is.Not.Empty);
        CharacterBaker bald = new(Copy("{\"locks\":[]}"));
        foreach (int direction in new[] { East, South })
        {
            Assert.That(bald.Render(idle, direction, 0).Count(CharacterMaterial.Hair), Is.LessThan(baker.Render(idle, direction, 0).Count(CharacterMaterial.Hair)), $"direction {direction}");
        }
    }

    /// <summary>
    /// The outfit's options draw as set: the project's adventurer wears long shirt sleeves, bare hands and no strap;
    /// short sleeves bare the forearms, gloves cover the hands, the strap crosses the chest, and without a skirt less
    /// of the tunic shows.
    /// </summary>
    [Test]
    public void TheOutfitsOptions_DrawAsTheRecipeSetsThem()
    {
        CharacterCanvas plain = baker.Render(idle, South, 0);
        Assert.That(recipe.Sleeves, Is.EqualTo(AdventurerSleeves.Long));
        Assert.That(MaterialOn(plain, CharacterMaterial.Skin, CharacterPart.LeftForearm, CharacterPart.RightForearm), Is.Zero);
        Assert.That(MaterialOn(plain, CharacterMaterial.Skin, CharacterPart.LeftHand, CharacterPart.RightHand), Is.Positive);
        Assert.That(MaterialOn(plain, CharacterMaterial.Belt, CharacterPart.Strap), Is.Zero);

        CharacterCanvas shortSleeves = new CharacterBaker(Copy("{\"sleeves\":1}")).Render(idle, South, 0);
        Assert.That(MaterialOn(shortSleeves, CharacterMaterial.Skin, CharacterPart.LeftForearm, CharacterPart.RightForearm), Is.Positive);
        Assert.That(MaterialOn(shortSleeves, CharacterMaterial.Jerkin, CharacterPart.LeftUpperArm, CharacterPart.RightUpperArm), Is.Positive);

        CharacterCanvas gloved = new CharacterBaker(Copy("{\"gloves\":true}")).Render(idle, South, 0);
        Assert.That(MaterialOn(gloved, CharacterMaterial.Skin, CharacterPart.LeftHand, CharacterPart.RightHand), Is.Zero);
        Assert.That(MaterialOn(gloved, CharacterMaterial.Gloves, CharacterPart.LeftHand, CharacterPart.RightHand), Is.Positive);

        CharacterCanvas strapped = new CharacterBaker(Copy("{\"strap\":true}")).Render(idle, South, 0);
        Assert.That(MaterialOn(strapped, CharacterMaterial.Belt, CharacterPart.Strap), Is.Positive);

        CharacterCanvas skirtless = new CharacterBaker(Copy("{\"skirtLength\":0}")).Render(idle, South, 0);
        Assert.That(skirtless.Count(CharacterMaterial.Jerkin), Is.LessThan(plain.Count(CharacterMaterial.Jerkin)));
    }

    /// <summary>
    /// Every clip's sheet has a row per direction of its frames, and no frame is drawn to its edge.
    /// </summary>
    [Test]
    public void EveryClip_HasARowPerDirection_AndFitsItsFrames()
    {
        Assert.That(recipe.CanGenerate(out string problem), Is.True, problem);
        foreach (CharacterClip clip in recipe.Clips)
        {
            CharacterSheet sheet = Sheets[clip.Name];
            Assert.That(sheet.Height, Is.EqualTo(CharacterDirections.Count * clip.FrameSize.y), clip.Name);
            Assert.That(sheet.Width, Is.EqualTo(clip.Motion.FrameCount * clip.FrameSize.x), clip.Name);
            Assert.That(sheet.FramesAtEdge, Is.Empty, clip.Name);
        }
    }

    /// <summary>
    /// The attacks and casts keyed on the rogue's body, the rogue's casts and the adventurer's own attacks, fit the
    /// adventurer's (<see cref="KeyedMotion.FitToBody"/>): in every frame the pelvis's height over the ankles and each
    /// foot's height over them are the same share of the legs' length, so a planted foot stays on the ground, and each
    /// hand is as far from its shoulder in arm lengths as on the rogue. On a copy of the rogue's body nothing changes.
    /// </summary>
    [Test]
    public void TheMotionsKeyedOnTheRogue_FitTheAdventurersBody()
    {
        CharacterRecipe rogue = AssetDatabase.LoadAssetAtPath<CharacterRecipe>(RoguePath);
        CharacterRecipe rogueCopy = Object.Instantiate(rogue);
        created.Add(rogueCopy);
        float legs = (recipe.Thigh + recipe.Shin) / (rogue.Thigh + rogue.Shin);
        float rogueArm = rogue.UpperArm + rogue.Forearm;
        float arm = recipe.UpperArm + recipe.Forearm;
        int motions = 0;
        foreach (CharacterClip clip in recipe.Clips)
        {
            if (clip.Motion is not KeyedMotion motion || motion.IsIdleAnimation)
            {
                continue;
            }

            motions++;
            Assert.That(motion.PosedOn, Is.EqualTo(rogue), clip.Name);
            for (int frame = 0; frame < motion.FrameCount; frame++)
            {
                string name = $"{clip.Name} {frame}";
                CharacterPose keyed = motion.PoseAt(frame, rogue, clip.Weapon);
                CharacterPose fitted = motion.PoseAt(frame, recipe, clip.Weapon);
                CharacterPose onCopy = motion.PoseAt(frame, rogueCopy, clip.Weapon);
                Assert.That(onCopy.PelvisHeight, Is.EqualTo(keyed.PelvisHeight), name);
                Assert.That(onCopy.LeftHand, Is.EqualTo(keyed.LeftHand), name);
                Assert.That(onCopy.RightFoot, Is.EqualTo(keyed.RightFoot), name);

                Assert.That(fitted.PelvisHeight - recipe.Ankle, Is.EqualTo((keyed.PelvisHeight - rogue.Ankle) * legs).Within(1e-4f), name);
                Assert.That(fitted.LeftFoot.z - recipe.Ankle, Is.EqualTo((keyed.LeftFoot.z - rogue.Ankle) * legs).Within(1e-4f), name);
                Assert.That(fitted.RightFoot.z - recipe.Ankle, Is.EqualTo((keyed.RightFoot.z - rogue.Ankle) * legs).Within(1e-4f), name);
                CharacterJoints keyedJoints = CharacterRig.Solve(rogue, keyed, clip.Weapon);
                CharacterJoints fittedJoints = CharacterRig.Solve(recipe, fitted, clip.Weapon);
                Assert.That(Vector3.Distance(fitted.LeftHand, fittedJoints.LeftShoulder) / arm, Is.EqualTo(Vector3.Distance(keyed.LeftHand, keyedJoints.LeftShoulder) / rogueArm).Within(1e-4f), name);
                Assert.That(Vector3.Distance(fitted.RightHand, fittedJoints.RightShoulder) / arm, Is.EqualTo(Vector3.Distance(keyed.RightHand, keyedJoints.RightShoulder) / rogueArm).Within(1e-4f), name);
            }
        }

        Assert.That(motions, Is.EqualTo(16), "The weapons' two attacks each and the standing casts.");
    }

    /// <summary>
    /// The adventurer's walk, run and sprint, and the casts drawn over its run, are its own motions, not the rogue's, the
    /// casts over its own run; the rogue's gaits stay plain, the head, the hair and the torso never moved by a style.
    /// </summary>
    [Test]
    public void TheGaits_AreTheAdventurersOwn_AndTheRoguesStayPlain()
    {
        CharacterRecipe rogue = AssetDatabase.LoadAssetAtPath<CharacterRecipe>(RoguePath);
        foreach (string name in new[] { "Walk", "Run", "Sprint", "CastSmiteRun", "CastWhirlRun" })
        {
            Assert.That(MotionOf(recipe, name), Is.Not.EqualTo(MotionOf(rogue, name)), name);
        }

        foreach (string name in new[] { "CastSmiteRun", "CastWhirlRun" })
        {
            Assert.That(((OverGaitMotion)MotionOf(recipe, name)).Legs, Is.EqualTo(MotionOf(recipe, "Run")), name);
        }

        foreach (GaitMotion gait in GaitsOf(rogue))
        {
            for (int frame = 0; frame < gait.FrameCount; frame++)
            {
                CharacterPose pose = gait.PoseAt(frame, rogue, null);
                Assert.That(pose.HeadLift, Is.Zero, $"{gait.name} {frame}");
                Assert.That(pose.HairLift, Is.Zero, $"{gait.name} {frame}");
                Assert.That(pose.Stretch, Is.Zero, $"{gait.name} {frame}");
            }
        }
    }

    /// <summary>
    /// Each of the adventurer's gaits bobs with its top and bottom on frames: the highest and lowest the pelvis gets in
    /// its frames are the highest and lowest it gets anywhere in the cycle.
    /// </summary>
    [Test]
    public void TheAdventurersGaits_BobWithTheirTopAndBottomOnFrames()
    {
        foreach (GaitMotion gait in GaitsOf(recipe))
        {
            float framesLow = float.MaxValue;
            float framesHigh = float.MinValue;
            for (int frame = 0; frame < gait.FrameCount; frame++)
            {
                float pelvis = gait.PoseAt(frame, recipe, null).PelvisHeight;
                framesLow = Mathf.Min(framesLow, pelvis);
                framesHigh = Mathf.Max(framesHigh, pelvis);
            }

            float low = float.MaxValue;
            float high = float.MinValue;
            for (int sample = 0; sample < 1000; sample++)
            {
                float pelvis = gait.PoseAtPhase(sample / 1000f, recipe, null).PelvisHeight;
                low = Mathf.Min(low, pelvis);
                high = Mathf.Max(high, pelvis);
            }

            Assert.That(high - low, Is.GreaterThan(1f), gait.name);
            Assert.That(framesLow, Is.EqualTo(low).Within(1e-4f), gait.name);
            Assert.That(framesHigh, Is.EqualTo(high).Within(1e-4f), gait.name);
        }
    }

    /// <summary>
    /// In each of the adventurer's gaits the head follows the bob late, reaching its lowest after the pelvis does; the
    /// torso is stretched on the frame the pelvis is highest and squashed on the one it is lowest; and the hair's tips
    /// move.
    /// </summary>
    [Test]
    public void TheAdventurersGaits_HeadFollowsTheBobLate_AndTheTorsoStretchesAtItsTop()
    {
        foreach (GaitMotion gait in GaitsOf(recipe))
        {
            float pelvisLow = float.MaxValue;
            float headLow = float.MaxValue;
            float pelvisLowAt = 0f;
            float headLowAt = 0f;
            for (int sample = 0; sample < 1000; sample++)
            {
                float phase = sample / 1000f;
                CharacterPose pose = gait.PoseAtPhase(phase, recipe, null);
                if (pose.PelvisHeight < pelvisLow)
                {
                    pelvisLow = pose.PelvisHeight;
                    pelvisLowAt = phase;
                }

                if (pose.PelvisHeight + pose.HeadLift < headLow)
                {
                    headLow = pose.PelvisHeight + pose.HeadLift;
                    headLowAt = phase;
                }
            }

            Assert.That(Mathf.Repeat(headLowAt - pelvisLowAt, 0.5f), Is.InRange(0.01f, 0.25f), gait.name);

            int highest = 0;
            int lowest = 0;
            bool hairMoves = false;
            for (int frame = 0; frame < gait.FrameCount; frame++)
            {
                CharacterPose pose = gait.PoseAt(frame, recipe, null);
                highest = pose.PelvisHeight > gait.PoseAt(highest, recipe, null).PelvisHeight ? frame : highest;
                lowest = pose.PelvisHeight < gait.PoseAt(lowest, recipe, null).PelvisHeight ? frame : lowest;
                hairMoves |= Mathf.Abs(pose.HairLift) > 0.1f;
            }

            Assert.That(gait.PoseAt(highest, recipe, null).Stretch, Is.GreaterThan(0f), gait.name);
            Assert.That(gait.PoseAt(lowest, recipe, null).Stretch, Is.LessThan(0f), gait.name);
            Assert.That(hairMoves, Is.True, gait.name);
        }
    }

    /// <summary>
    /// In each of the adventurer's gaits a hand at the end of its swing is higher over the pelvis than at its middle, by
    /// more than half a rig unit, like a pendulum; in the rogue's it is as high.
    /// </summary>
    [Test]
    public void TheAdventurersGaits_LiftTheHandsAtBothEndsOfTheirSwing()
    {
        CharacterRecipe rogue = AssetDatabase.LoadAssetAtPath<CharacterRecipe>(RoguePath);
        foreach (GaitMotion gait in GaitsOf(recipe))
        {
            Assert.That(SwingEndRise(gait, recipe), Is.GreaterThan(0.5f), gait.name);
        }

        foreach (GaitMotion gait in GaitsOf(rogue))
        {
            Assert.That(SwingEndRise(gait, rogue), Is.EqualTo(0f).Within(1e-4f), gait.name);
        }
    }

    /// <summary>
    /// The adventurer's five idle animations are keyed motions posed on its own body, flagged as idle animations, with
    /// no contact frame; each starts and ends on the idle's pose, and its first frame is the idle's to the pixel in
    /// every direction, so it starts and ends without a pop.
    /// </summary>
    [Test]
    public void TheIdleAnimations_StartAndEndOnTheIdle_WithoutAContactFrame()
    {
        CharacterPose standing = MotionOf(recipe, "Idle").PoseAt(0, recipe, null);
        CharacterSheet idleSheet = Sheets["Idle"];
        foreach (string name in IdleAnimations)
        {
            KeyedMotion motion = (KeyedMotion)MotionOf(recipe, name);
            Assert.That(motion.IsIdleAnimation, Is.True, name);
            Assert.That(motion.ContactFrame, Is.EqualTo(-1), name);
            Assert.That(motion.PosedOn, Is.EqualTo(recipe), name);
            foreach (float time in new[] { 0f, 1f })
            {
                CharacterPose pose = motion.PoseAtTime(time, recipe, null);
                Assert.That(pose.PelvisHeight, Is.EqualTo(standing.PelvisHeight).Within(1e-3f), $"{name} at {time}");
                Assert.That(pose.Lean, Is.EqualTo(standing.Lean).Within(1e-3f), $"{name} at {time}");
                Assert.That(Vector3.Distance(pose.LeftHand, standing.LeftHand), Is.LessThan(1e-3f), $"{name} at {time}");
                Assert.That(Vector3.Distance(pose.RightHand, standing.RightHand), Is.LessThan(1e-3f), $"{name} at {time}");
                Assert.That(Vector3.Distance(pose.LeftFoot, standing.LeftFoot), Is.LessThan(1e-3f), $"{name} at {time}");
                Assert.That(Vector3.Distance(pose.RightFoot, standing.RightFoot), Is.LessThan(1e-3f), $"{name} at {time}");
            }

            CharacterSheet sheet = Sheets[name];
            for (int direction = 0; direction < CharacterDirections.Count; direction++)
            {
                Assert.That(FramePixels(sheet, direction, 0), Is.EqualTo(FramePixels(idleSheet, direction, 0)), $"{name} {CharacterDirections.Name(direction)}");
            }
        }
    }

    /// <summary>
    /// The adventurer's attacks are its own: the first of each weapon's pair is not the rogue's motion and the second is
    /// one the rogue lacks, both keyed motions with the weapon's art. Each ends with the pelvis, the shoulders' twist, the
    /// hands and the weapon (the blade, or the bow and its draw) where the other starts, so attacking again carries on from
    /// where the last attack stopped. Their head follows its own rise and fall late and the torso stretches or squashes on
    /// some frame, as in the gaits, while the rogue's attacks stay plain.
    /// </summary>
    [Test]
    public void TheAttacks_ComeInPairsThatFlowIntoEachOther_StyledLikeTheGaits()
    {
        CharacterRecipe rogue = AssetDatabase.LoadAssetAtPath<CharacterRecipe>(RoguePath);
        foreach (string[] pair in AttackPairs)
        {
            CharacterClip first = recipe.Clips.First(clip => clip.Name == pair[0]);
            CharacterClip second = recipe.Clips.First(clip => clip.Name == pair[1]);
            Assert.That(first.Motion, Is.Not.EqualTo(MotionOf(rogue, pair[0])), pair[0]);
            Assert.That(rogue.Clips.Any(clip => clip.Name == pair[1]), Is.False, pair[1]);
            Assert.That(second.Weapon, Is.EqualTo(first.Weapon), pair[1]);
            Assert.That(first.Motion, Is.InstanceOf<KeyedMotion>(), pair[0]);
            Assert.That(second.Motion, Is.InstanceOf<KeyedMotion>(), pair[1]);
            foreach ((CharacterClip ending, CharacterClip next) in new[] { (first, second), (second, first) })
            {
                string name = $"{ending.Name} into {next.Name}";
                CharacterPose end = ending.Motion.PoseAtTime(1f, recipe, ending.Weapon);
                CharacterPose start = next.Motion.PoseAtTime(0f, recipe, next.Weapon);
                Assert.That(end.PelvisHeight, Is.EqualTo(start.PelvisHeight).Within(0.05f), name);
                Assert.That(end.Twist, Is.EqualTo(start.Twist).Within(0.05f), name);
                Assert.That(Vector3.Distance(end.LeftHand, start.LeftHand), Is.LessThan(0.05f), name);
                Assert.That(Vector3.Distance(end.RightHand, start.RightHand), Is.LessThan(0.05f), name);
                Assert.That(Vector3.Distance(end.Blade, start.Blade), Is.LessThan(0.01f), name);
                Assert.That(Vector3.Distance(end.BowUp, start.BowUp), Is.LessThan(0.01f), name);
                Assert.That(end.Draw, Is.EqualTo(start.Draw).Within(0.01f), name);
            }

            foreach (CharacterClip clip in new[] { first, second })
            {
                float lift = 0f;
                float stretch = 0f;
                for (int frame = 0; frame < clip.Motion.FrameCount; frame++)
                {
                    CharacterPose pose = clip.Motion.PoseAt(frame, recipe, clip.Weapon);
                    lift = Mathf.Max(lift, Mathf.Abs(pose.HeadLift));
                    stretch = Mathf.Max(stretch, Mathf.Abs(pose.Stretch));
                }

                Assert.That(lift, Is.GreaterThan(0.2f), $"{clip.Name}: the head follows late.");
                Assert.That(stretch, Is.GreaterThan(0.05f), $"{clip.Name}: the torso stretches or squashes.");
            }

            CharacterMotion rogues = MotionOf(rogue, pair[0]);
            for (int frame = 0; frame < rogues.FrameCount; frame++)
            {
                CharacterPose pose = rogues.PoseAt(frame, rogue, first.Weapon);
                Assert.That(pose.HeadLift, Is.Zero, $"the rogue's {pair[0]} {frame}");
                Assert.That(pose.HairLift, Is.Zero, $"the rogue's {pair[0]} {frame}");
                Assert.That(pose.Stretch, Is.Zero, $"the rogue's {pair[0]} {frame}");
            }
        }
    }

    /// <summary>
    /// The adventurer's ten casts are its own keyed motions, not the rogue's, styled like its attacks: the head follows its
    /// own rise and fall late and the torso stretches or squashes on some frame, while the rogue's casts stay plain. The
    /// whirl and the sweep spin a whole turn, starting and ending facing the way the character faces; no other cast
    /// spins. The casts over the run draw its own smite and whirl. Its roll draws no weapon, where the rogue's holds the
    /// bow (Docs/PlayerCharacter.md, Phase 4).
    /// </summary>
    [Test]
    public void TheCasts_AreTheAdventurersOwn_TheWhirlAndTheSweepSpinning()
    {
        CharacterRecipe rogue = AssetDatabase.LoadAssetAtPath<CharacterRecipe>(RoguePath);
        foreach (string name in Casts)
        {
            CharacterClip clip = recipe.Clips.First(c => c.Name == name);
            Assert.That(clip.Motion, Is.InstanceOf<KeyedMotion>(), name);
            Assert.That(clip.Motion, Is.Not.EqualTo(MotionOf(rogue, name)), name);
            float lift = 0f;
            float stretch = 0f;
            float spin = 0f;
            for (int frame = 0; frame < clip.Motion.FrameCount; frame++)
            {
                CharacterPose pose = clip.Motion.PoseAt(frame, recipe, clip.Weapon);
                lift = Mathf.Max(lift, Mathf.Abs(pose.HeadLift));
                stretch = Mathf.Max(stretch, Mathf.Abs(pose.Stretch));
                spin = Mathf.Max(spin, Mathf.Abs(pose.Spin));
            }

            Assert.That(lift, Is.GreaterThan(0.1f), $"{name}: the head follows late.");
            Assert.That(stretch, Is.GreaterThan(0.05f), $"{name}: the torso stretches or squashes.");
            if (name == "CastWhirl" || name == "CastSweep")
            {
                Assert.That(spin, Is.GreaterThan(90f), name);
                Assert.That(clip.Motion.PoseAtTime(0f, recipe, clip.Weapon).Spin, Is.Zero, name);
                Assert.That(clip.Motion.PoseAtTime(1f, recipe, clip.Weapon).Spin, Is.EqualTo(360f).Within(1e-3f), name);
            }
            else
            {
                Assert.That(spin, Is.Zero, name);
            }

            CharacterClip roguesClip = rogue.Clips.First(c => c.Name == name);
            for (int frame = 0; frame < roguesClip.Motion.FrameCount; frame++)
            {
                CharacterPose pose = roguesClip.Motion.PoseAt(frame, rogue, roguesClip.Weapon);
                Assert.That(pose.HeadLift, Is.Zero, $"the rogue's {name} {frame}");
                Assert.That(pose.Stretch, Is.Zero, $"the rogue's {name} {frame}");
                Assert.That(pose.Spin, Is.Zero, $"the rogue's {name} {frame}");
            }
        }

        Assert.That(((OverGaitMotion)MotionOf(recipe, "CastSmiteRun")).Upper, Is.EqualTo(MotionOf(recipe, "CastSmite")));
        Assert.That(((OverGaitMotion)MotionOf(recipe, "CastWhirlRun")).Upper, Is.EqualTo(MotionOf(recipe, "CastWhirl")));
        Assert.That(recipe.Clips.First(c => c.Name == "CastRoll").Weapon, Is.Null);
        Assert.That(rogue.Clips.First(c => c.Name == "CastRoll").Weapon, Is.Not.Null);
    }

    /// <summary>
    /// A spin turns the whole character, weapon and smear included, while the light stays where it is: the lunge with
    /// every key spun a quarter turn draws, facing east, what the plain lunge draws facing north, frame for frame.
    /// </summary>
    [Test]
    public void ASpin_TurnsTheWholeCharacter()
    {
        CharacterClip lunge = recipe.Clips.First(clip => clip.Name == "CastLunge");
        KeyedMotion spun = Object.Instantiate((KeyedMotion)lunge.Motion);
        created.Add(spun);
        SerializedObject serialized = new(spun);
        SerializedProperty keys = serialized.FindProperty("keys");
        for (int i = 0; i < keys.arraySize; i++)
        {
            keys.GetArrayElementAtIndex(i).FindPropertyRelative("spin").floatValue = 90f;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        CharacterSheet plain = baker.Bake(lunge);
        CharacterSheet turned = baker.Bake(new CharacterClip("Spun", spun, lunge.Weapon, lunge.FrameSize, lunge.Pivot));
        Assert.That(lunge.Motion.TryGetSmear(4, out _), Is.True, "The lunge's release frame has a smear.");
        for (int frame = 0; frame < plain.FrameCount; frame++)
        {
            Assert.That(FramePixels(turned, East, frame), Is.EqualTo(FramePixels(plain, North, frame)), $"frame {frame}");
        }
    }

    /// <summary>
    /// A cast over the run takes the cast's spin, and adds the cast's stretch and its head's and hair's lift to the
    /// run's, so the whirl spins over the running legs and the run's bob still shows under it.
    /// </summary>
    [Test]
    public void ACastOverTheRun_TakesTheCastsSpin_AndAddsItsStyleToTheRuns()
    {
        CharacterClip clip = recipe.Clips.First(c => c.Name == "CastWhirlRun");
        OverGaitMotion whirlRun = (OverGaitMotion)clip.Motion;
        int frames = whirlRun.Upper.FrameCount;
        bool spins = false;
        for (int frame = 0; frame < whirlRun.FrameCount; frame++)
        {
            string name = $"frame {frame}";
            CharacterPose legs = whirlRun.Legs.PoseAt(frame / frames, recipe, null);
            CharacterPose upper = whirlRun.Upper.PoseAt(frame % frames, recipe, clip.Weapon);
            CharacterPose pose = whirlRun.PoseAt(frame, recipe, clip.Weapon);
            Assert.That(pose.Spin, Is.EqualTo(upper.Spin), name);
            Assert.That(pose.Stretch, Is.EqualTo(legs.Stretch + upper.Stretch).Within(1e-5f), name);
            Assert.That(pose.HeadLift, Is.EqualTo(legs.HeadLift + upper.HeadLift).Within(1e-5f), name);
            Assert.That(pose.HairLift, Is.EqualTo(legs.HairLift + upper.HairLift).Within(1e-5f), name);
            spins |= pose.Spin > 90f;
        }

        Assert.That(spins, Is.True);
    }

    /// <summary>
    /// Facing south, on every frame of the yawn whose pose shuts the eyes each eye is a line without its glint, and on
    /// every frame whose pose opens the mouth the rows below its line are dark, so a yawn reads at this size.
    /// </summary>
    [Test]
    public void TheYawn_ShutsTheEyes_AndOpensTheMouth()
    {
        CharacterClip yawn = recipe.Clips.First(clip => clip.Name == "IdleYawn");
        CharacterCanvas standing = baker.Render(idle, South, 0);
        int mouthWidth = Mathf.RoundToInt(recipe.Mouth.y);
        int shut = 0;
        int open = 0;
        for (int frame = 0; frame < yawn.Motion.FrameCount; frame++)
        {
            CharacterPose pose = yawn.Motion.PoseAt(frame, recipe, null);
            CharacterCanvas canvas = baker.Render(yawn, South, frame);
            if (pose.EyesClosed > 0.5f)
            {
                shut++;
                Assert.That(canvas.Count(CharacterMaterial.Eye), Is.EqualTo(2 * recipe.EyeWidth), $"frame {frame}");
                Assert.That(canvas.Count(CharacterMaterial.EyeGlint), Is.Zero, $"frame {frame}");
            }

            int rows = Mathf.RoundToInt(pose.MouthOpen);
            if (rows > 0)
            {
                open++;
                Assert.That(canvas.Count(CharacterMaterial.Mouth), Is.EqualTo(mouthWidth), $"frame {frame}");
                Assert.That(canvas.Count(CharacterMaterial.Brow), Is.GreaterThanOrEqualTo(standing.Count(CharacterMaterial.Brow) + mouthWidth), $"frame {frame}");
            }
        }

        Assert.That(shut, Is.GreaterThan(0));
        Assert.That(open, Is.GreaterThan(0));
    }

    /// <summary>
    /// Each of the project's adventurer sheets is what the recipe bakes, sliced per direction and frame at 32 pixels per
    /// unit with its pivot on the feet (<see cref="CharacterSheetAssert.SheetsAreWhatTheRecipeBakes"/>).
    /// </summary>
    [Test]
    public void TheProjectsSheets_AreWhatTheRecipeBakes_SlicedPerDirectionAndFrame()
    {
        CharacterSheetAssert.SheetsAreWhatTheRecipeBakes(recipe, Sheets);
    }

    /// <summary>
    /// The project's adventurer animation set holds every clip the recipe bakes, in order
    /// (<see cref="CharacterSheetAssert.SetHoldsWhatTheRecipeBakes"/>).
    /// </summary>
    [Test]
    public void TheProjectsAnimationSet_HoldsWhatTheRecipeBakes()
    {
        CharacterSheetAssert.SetHoldsWhatTheRecipeBakes(recipe, baker);
    }

    /// <summary>
    /// The pixels of <paramref name="sheet"/>'s frame <paramref name="frame"/> facing <paramref name="direction"/>.
    /// </summary>
    private static Color32[] FramePixels(CharacterSheet sheet, int direction, int frame)
    {
        RectInt rect = CharacterSheet.FrameRect(direction, frame, sheet.Clip.FrameSize);
        Color32[] pixels = new Color32[rect.width * rect.height];
        for (int y = 0; y < rect.height; y++)
        {
            for (int x = 0; x < rect.width; x++)
            {
                pixels[y * rect.width + x] = sheet.Pixels[(rect.y + y) * sheet.Width + rect.x + x];
            }
        }

        return pixels;
    }

    /// <summary>
    /// The motion of <paramref name="owner"/>'s clip named <paramref name="name"/>.
    /// </summary>
    private static CharacterMotion MotionOf(CharacterRecipe owner, string name)
    {
        return owner.Clips.First(clip => clip.Name == name).Motion;
    }

    /// <summary>
    /// The gaits <paramref name="owner"/>'s clips play without a weapon: its walk, run and sprint.
    /// </summary>
    private static IEnumerable<GaitMotion> GaitsOf(CharacterRecipe owner)
    {
        GaitMotion[] gaits = owner.Clips.Where(clip => clip.Weapon == null).Select(clip => clip.Motion).OfType<GaitMotion>().Distinct().ToArray();
        Assert.That(gaits.Length, Is.EqualTo(3), owner.name);
        return gaits;
    }

    /// <summary>
    /// How much higher over the pelvis the left hand is at the back end of its swing (the cycle's start) than at its
    /// middle (a quarter in), on <paramref name="owner"/>'s body.
    /// </summary>
    private static float SwingEndRise(GaitMotion gait, CharacterRecipe owner)
    {
        CharacterPose end = gait.PoseAtPhase(0f, owner, null);
        CharacterPose middle = gait.PoseAtPhase(0.25f, owner, null);
        return end.LeftHand.z - end.PelvisHeight - (middle.LeftHand.z - middle.PelvisHeight);
    }

    /// <summary>
    /// A copy of the project's recipe with <paramref name="json"/>'s fields overwritten, destroyed after the test.
    /// </summary>
    private AdventurerRecipe Copy(string json)
    {
        AdventurerRecipe copy = Object.Instantiate(recipe);
        created.Add(copy);
        JsonUtility.FromJsonOverwrite(json, copy);
        return copy;
    }

    /// <summary>
    /// How many of <paramref name="canvas"/>'s pixels belong to <paramref name="part"/>.
    /// </summary>
    private static int PartPixels(CharacterCanvas canvas, CharacterPart part)
    {
        return MaterialOn(canvas, null, part);
    }

    /// <summary>
    /// How many of <paramref name="canvas"/>'s pixels belong to one of <paramref name="parts"/> and, when given, are
    /// <paramref name="material"/>.
    /// </summary>
    private static int MaterialOn(CharacterCanvas canvas, CharacterMaterial? material, params CharacterPart[] parts)
    {
        int count = 0;
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                if (canvas.IsFilled(x, y) && parts.Contains(canvas.PartAt(x, y)) && (material == null || canvas.MaterialAt(x, y) == material))
                {
                    count++;
                }
            }
        }

        return count;
    }
}
