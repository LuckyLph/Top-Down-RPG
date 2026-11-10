using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for the adventurer's character recipe, the player's base body (Docs/PlayerCharacter.md, Phases 0 to 7), on the
/// project's asset, which they only read, and on copies made in memory: baking gives the same pixels every time; the
/// standing adventurer is its target height; its face shows both eyes, their glints, the brows and the mouth from the
/// front, one eye in profile and nothing from behind; its ears show beside the head; its locks grow the hair past the
/// head's ball from the front and in profile; the outfit draws as its parts are set (Docs/PlayerCharacter.md, Phase 6:
/// sleeves, tops, legs, boots, capes, the hood and the cap; Phase 7: gloves and the satchel, its pouch showing), every
/// layer drawing only its own in steps of its own, the project's parts name every option's layers, and the layer files
/// compose exactly what the editor bakes; each weapon's gear shows the weapon stowed where it is carried in the clips
/// without a weapon, only what stays on in its own clips and nothing in another weapon's, its weapon in the weapon's
/// colours, and over a cape it shows further out; every clip has a row per direction and fits its frames; the attacks and casts
/// keyed on the rogue's body fit its body; its gaits are its own, styled, while the rogue's stay plain: the bob's top and
/// bottom on frames, the head following it late, the torso stretching at its top and squashing at its bottom, the
/// hair's tips moving, and the hands rising at both ends of their swing; its idle animations start and end on the
/// idle's pose, the first frame the idle's to the pixel, without a contact frame; the yawn shuts the eyes and opens the
/// mouth; its attacks are its own, two a weapon, each ending with the weapon where the other starts, styled while the
/// rogue's stay plain; its casts are its own and styled, the whirl and the sweep spinning a whole turn and the roll
/// unarmed, while the rogue's stay plain; a spin turns the whole character; a cast over the run takes the cast's spin and
/// adds its style to the run's; its palette maps name the dye and tone of every pixel a player colours, in the colours
/// the recipe gives the dye's material, and none for the weapons, the casts' light and the inside of the mouth; its set
/// names the dyes its sheets carry, and the outfit's options carry their own; and the project's sheets, their palette
/// maps and the animation set are what the recipe bakes.
/// </summary>
public class AdventurerRecipeTests
{
    private const string RecipePath = "Assets/Settings/Characters/Recipe_Adventurer.asset";
    private const string RoguePath = "Assets/Settings/Characters/Recipe_Rogue.asset";
    private const string PartsPath = "Assets/Sprites/Characters/Adventurer/Parts/Adventurer_Parts.asset";
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
        AdventurerHair tousled = recipe.Hairs[0];
        Assert.That(tousled.Locks, Is.Not.Empty);
        string bald = JsonUtility.ToJson(new AdventurerHair("Bald", tousled.Face, tousled.Fringe, new HairLock[0]));
        CharacterBaker without = new(Copy($"{{\"hairOptions\":[{bald}]}}"));
        foreach (int direction in new[] { East, South })
        {
            Assert.That(without.Render(idle, direction, 0).Count(CharacterMaterial.Hair), Is.LessThan(baker.Render(idle, direction, 0).Count(CharacterMaterial.Hair)), $"direction {direction}");
        }
    }

    /// <summary>
    /// The outfit draws as set (Docs/PlayerCharacter.md, Phase 6): the project's adventurer wears its first part of each
    /// slot, long shirt sleeves, bare hands and no satchel; short sleeves bare the forearms, the shirt alone shows no
    /// tunic, the long tunic more; gloves cover the hands and the satchel's strap crosses the chest in a material of its
    /// own, its pouch showing behind the right hip from the side and from behind (Phase 7); shorts bare the shins; short boots show less boot; a cape hangs lined behind, or unlined; a hood
    /// shows the face in its opening and its inside around it, hiding the locks, and a cap hides the tufts on top.
    /// </summary>
    [Test]
    public void TheOutfit_DrawsAsItsPartsAreSet()
    {
        CharacterCanvas plain = baker.Render(idle, South, 0);
        Assert.That(recipe.Tops[0].Sleeves, Is.EqualTo(AdventurerSleeves.Long));
        Assert.That(MaterialOn(plain, CharacterMaterial.Skin, CharacterPart.LeftForearm, CharacterPart.RightForearm), Is.Zero);
        Assert.That(MaterialOn(plain, CharacterMaterial.Skin, CharacterPart.LeftHand, CharacterPart.RightHand), Is.Positive);
        Assert.That(plain.Count(CharacterMaterial.Strap) + plain.Count(CharacterMaterial.Pouch), Is.Zero);
        Assert.That(plain.Count(CharacterMaterial.Headwear) + plain.Count(CharacterMaterial.Cloak), Is.Zero);

        CharacterCanvas shortSleeves = Outfit(CharacterSlot.Top, "Short sleeves", South);
        Assert.That(MaterialOn(shortSleeves, CharacterMaterial.Skin, CharacterPart.LeftForearm, CharacterPart.RightForearm), Is.Positive);
        Assert.That(MaterialOn(shortSleeves, CharacterMaterial.Jerkin, CharacterPart.LeftUpperArm, CharacterPart.RightUpperArm), Is.Positive);
        Assert.That(Outfit(CharacterSlot.Top, "Shirt", South).Count(CharacterMaterial.Jerkin), Is.Zero);
        Assert.That(Outfit(CharacterSlot.Top, "Long tunic", South).Count(CharacterMaterial.Jerkin), Is.GreaterThan(plain.Count(CharacterMaterial.Jerkin)));

        CharacterCanvas gloved = Outfit(CharacterSlot.Hands, "Gloves", South);
        Assert.That(MaterialOn(gloved, CharacterMaterial.Skin, CharacterPart.LeftHand, CharacterPart.RightHand), Is.Zero);
        Assert.That(MaterialOn(gloved, CharacterMaterial.Gloves, CharacterPart.LeftHand, CharacterPart.RightHand), Is.Positive);
        CharacterCanvas strapped = Outfit(CharacterSlot.Bag, "Satchel", South);
        Assert.That(MaterialOn(strapped, CharacterMaterial.Strap, CharacterPart.Strap), Is.Positive);
        Assert.That(MaterialOn(strapped, CharacterMaterial.Belt, CharacterPart.Strap), Is.Zero);
        Assert.That(Outfit(CharacterSlot.Bag, "Satchel", East).Count(CharacterMaterial.Pouch), Is.Positive, "the pouch from the side");
        Assert.That(Outfit(CharacterSlot.Bag, "Satchel", North).Count(CharacterMaterial.Pouch), Is.Positive, "the pouch from behind");

        baker.Outfit = Picks((CharacterSlot.Legs, "Shorts"), (CharacterSlot.Boots, "Short"));
        CharacterCanvas shorts = baker.Render(idle, South, 0);
        baker.Outfit = default;
        Assert.That(MaterialOn(plain, CharacterMaterial.Skin, CharacterPart.LeftShin, CharacterPart.RightShin), Is.Zero);
        Assert.That(MaterialOn(shorts, CharacterMaterial.Skin, CharacterPart.LeftShin, CharacterPart.RightShin), Is.Positive);
        Assert.That(Outfit(CharacterSlot.Boots, "Short", South).Count(CharacterMaterial.Boots), Is.LessThan(plain.Count(CharacterMaterial.Boots)));

        CharacterCanvas cape = Outfit(CharacterSlot.Cape, "Cape", North);
        Assert.That(cape.Count(CharacterMaterial.Cloak), Is.Positive);
        Assert.That(cape.Count(CharacterMaterial.Lining), Is.Positive);
        CharacterCanvas unlined = Outfit(CharacterSlot.Cape, "Unlined", North);
        Assert.That(unlined.Count(CharacterMaterial.Cloak), Is.Positive);
        Assert.That(unlined.Count(CharacterMaterial.Lining), Is.Zero);

        CharacterCanvas hood = Outfit(CharacterSlot.Headwear, "Hood", South);
        Assert.That(hood.Count(CharacterMaterial.Headwear), Is.Positive);
        Assert.That(hood.Count(CharacterMaterial.HoodInside), Is.Positive);
        Assert.That(MaterialOn(hood, CharacterMaterial.Skin, CharacterPart.Head), Is.Positive, "the face shows in the opening");
        Assert.That(hood.Count(CharacterMaterial.Hair), Is.LessThan(plain.Count(CharacterMaterial.Hair)), "the hood hides the locks");
        CharacterCanvas cap = Outfit(CharacterSlot.Headwear, "Cap", East);
        Assert.That(cap.Count(CharacterMaterial.Headwear), Is.Positive);
        Assert.That(cap.Count(CharacterMaterial.Hair), Is.LessThan(baker.Render(idle, East, 0).Count(CharacterMaterial.Hair)), "the cap hides the tufts");
    }

    /// <summary>
    /// Every layer draws only its own (Docs/PlayerCharacter.md, Phases 6 and 7): the body none of the parts' materials
    /// and skin only on the head and ears, each hairstyle only hair, the headwear only itself and its inside, the tops
    /// only the tunic, the shirt and bare forearms, the capes only the cape and its lining, the legs only trousers and
    /// bare shins, the boots only boots, the hands only skin or gloves, the bag only its strap and pouch, and a weapon's
    /// gear only the weapon, its leather, fittings and strap; and every layer draws in its own drawing steps.
    /// </summary>
    [Test]
    public void EveryLayer_DrawsOnlyItsOwn()
    {
        IReadOnlyList<CharacterLayerKey> keys = baker.LayerKeys();
        Assert.That(keys.Count, Is.EqualTo(1 + 5 * 3 + 3 + 5 + 4 + 2 + 2 + 2 + 2 + 4 * 4));
        CharacterLayerClip[] layers = baker.BakeLayers(idle, keys);
        Dictionary<int, CharacterMaterial[]> allowed = new()
        {
            { (int)CharacterSlot.Hair, new[] { CharacterMaterial.Hair } },
            { (int)CharacterSlot.Headwear, new[] { CharacterMaterial.Headwear, CharacterMaterial.HoodInside } },
            { (int)CharacterSlot.Top, new[] { CharacterMaterial.Jerkin, CharacterMaterial.Shirt, CharacterMaterial.Skin } },
            { (int)CharacterSlot.Cape, new[] { CharacterMaterial.Cloak, CharacterMaterial.Lining } },
            { (int)CharacterSlot.Legs, new[] { CharacterMaterial.Trousers, CharacterMaterial.Skin } },
            { (int)CharacterSlot.Boots, new[] { CharacterMaterial.Boots } },
            { (int)CharacterSlot.Hands, new[] { CharacterMaterial.Skin, CharacterMaterial.Gloves } },
            { (int)CharacterSlot.Bag, new[] { CharacterMaterial.Strap, CharacterMaterial.Pouch } },
            { (int)CharacterSlot.Weapon, new[] { CharacterMaterial.Steel, CharacterMaterial.Guard, CharacterMaterial.Grip, CharacterMaterial.Wood, CharacterMaterial.BowString, CharacterMaterial.Shaft, CharacterMaterial.Fletching, CharacterMaterial.Belt, CharacterMaterial.Fittings, CharacterMaterial.Strap } }
        };
        CharacterMaterial[] partsOnly = { CharacterMaterial.Hair, CharacterMaterial.Headwear, CharacterMaterial.HoodInside, CharacterMaterial.Jerkin, CharacterMaterial.Shirt, CharacterMaterial.Cloak, CharacterMaterial.Lining, CharacterMaterial.Trousers, CharacterMaterial.Boots, CharacterMaterial.Gloves, CharacterMaterial.Strap, CharacterMaterial.Pouch };
        CharacterPart[] bodySkin = { CharacterPart.Head, CharacterPart.LeftEar, CharacterPart.RightEar };
        Dictionary<int, HashSet<int>> steps = new();
        for (int k = 0; k < keys.Count; k++)
        {
            foreach (CharacterLayerFrame frame in layers[k].Frames)
            {
                for (int i = 0; i < frame.Material.Length; i++)
                {
                    if (frame.Material[i] == CharacterLayer.Empty)
                    {
                        continue;
                    }

                    CharacterMaterial material = (CharacterMaterial)frame.Material[i];
                    bool fits = keys[k].IsBase ? !partsOnly.Contains(material) : allowed[keys[k].Slot].Contains(material);
                    Assert.That(fits, Is.True, $"layer {keys[k].Slot} {keys[k].Option} draws {material}");
                    if (keys[k].IsBase && material == CharacterMaterial.Skin)
                    {
                        Assert.That(bodySkin, Does.Contain((CharacterPart)frame.Part[i]), "the body's skin is the head's and the ears'");
                    }

                    if (!steps.TryGetValue(frame.Step[i], out HashSet<int> owners))
                    {
                        steps[frame.Step[i]] = owners = new HashSet<int>();
                    }

                    owners.Add(keys[k].Slot);
                }
            }
        }

        foreach (KeyValuePair<int, HashSet<int>> step in steps)
        {
            Assert.That(step.Value.Count, Is.EqualTo(1), $"step {step.Key} is drawn by one layer");
        }
    }

    /// <summary>
    /// The project's parts (Docs/PlayerCharacter.md, Phase 6) are the recipe's: over the adventurer's set and its body's
    /// layer file, with the recipe's line gap, a slot per <see cref="CharacterSlot"/> in order with the recipe's options
    /// by name, the hair drawn under each headwear (a layer per headwear option), an option drawing nothing (no
    /// headwear, no cape, no bag, no weapon's gear) without a layer, and each option naming the dyes its layers draw; the
    /// weapon's slot follows the weapon, drawn under each cape, a weapon's gear named by its name and none for a weapon
    /// without gear, the sword at the hip alike under every cape, the greatsword and the longbow on the back further out
    /// over a cape, alike under every cape (Docs/PlayerCharacter.md, Phase 7).
    /// </summary>
    [Test]
    public void TheProjectsParts_NameEveryOptionsLayers()
    {
        CharacterParts parts = AssetDatabase.LoadAssetAtPath<CharacterParts>(PartsPath);
        Assert.That(parts, Is.Not.Null);
        Assert.That(parts.Look, Is.EqualTo(AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(CharacterSheetWriter.SetPath(recipe))));
        Assert.That(AssetDatabase.GetAssetPath(parts.Body), Does.EndWith("Adventurer_Body.bytes"));
        Assert.That(parts.InnerLineGap, Is.EqualTo(recipe.InnerLineGap));
        Assert.That(parts.Slots.Select(slot => slot.Slot), Is.EqualTo(System.Enum.GetValues(typeof(CharacterSlot))));
        foreach (CharacterPartSlot slot in parts.Slots)
        {
            Assert.That(slot.Options.Select(option => option.Name), Is.EqualTo(Enumerable.Range(0, recipe.PartOptions(slot.Slot)).Select(o => recipe.PartName(slot.Slot, o))), slot.Slot.ToString());
            Assert.That(slot.VariesWith, Is.EqualTo(recipe.PartVariesWith(slot.Slot)));
            int layers = slot.VariesWith >= 0 ? recipe.PartOptions((CharacterSlot)slot.VariesWith) : 1;
            foreach (CharacterPartOption option in slot.Options)
            {
                Assert.That(option.Layers.Count, Is.EqualTo(layers), $"{slot.Slot} {option.Name}");
            }
        }

        Assert.That(parts.Get(CharacterSlot.Headwear).Options[0].Layers[0], Is.Null, "no headwear draws nothing");
        Assert.That(parts.Get(CharacterSlot.Cape).Options[0].Layers[0], Is.Null, "no cape draws nothing");
        Assert.That(parts.Get(CharacterSlot.Headwear).Options[1].Dyes & (1 << (int)CharacterDye.Headwear), Is.Not.Zero);
        Assert.That(parts.Get(CharacterSlot.Cape).Options[1].Dyes & (1 << (int)CharacterDye.CapeLining), Is.Not.Zero);
        Assert.That(parts.Get(CharacterSlot.Cape).Options[2].Dyes & (1 << (int)CharacterDye.CapeLining), Is.Zero, "unlined");
        Assert.That(parts.Get(CharacterSlot.Bag).Options[0].Layers[0], Is.Null, "no bag draws nothing");
        Assert.That(parts.Get(CharacterSlot.Bag).Options[1].Dyes & (1 << (int)CharacterDye.Pouch), Is.Not.Zero);
        Assert.That(parts.Get(CharacterSlot.Hands).Options[1].Dyes & (1 << (int)CharacterDye.Gloves), Is.Not.Zero);
        Assert.That(parts.Slots.Where(slot => slot.FollowsWeapon).Select(slot => slot.Slot), Is.EqualTo(new[] { CharacterSlot.Weapon }));

        CharacterPartSlot weapons = parts.Get(CharacterSlot.Weapon);
        Assert.That(weapons.Options.Select(option => option.Name), Is.EqualTo(new[] { "Sword", "Greatsword", "Longbow", "None" }));
        Assert.That(weapons.VariesWith, Is.EqualTo((int)CharacterSlot.Cape));
        Assert.That(weapons.GearOption("Longbow"), Is.EqualTo(2));
        Assert.That(weapons.GearOption("Halberd"), Is.EqualTo(3), "a weapon without gear shows none");
        Assert.That(weapons.GearOption(string.Empty), Is.EqualTo(3));
        Assert.That(weapons.Options[3].DrawsNothing, Is.True);
        Assert.That(weapons.Options[0].Layers.Distinct().Count(), Is.EqualTo(1), "the sword at the hip is alike under every cape");
        foreach (CharacterPartOption onTheBack in new[] { weapons.Options[1], weapons.Options[2] })
        {
            Assert.That(onTheBack.Layers[1], Is.Not.EqualTo(onTheBack.Layers[0]), $"{onTheBack.Name} sits further out over a cape");
            Assert.That(onTheBack.Layers.Skip(1).Distinct().Count(), Is.EqualTo(1), $"{onTheBack.Name} is alike under every cape");
        }
    }

    /// <summary>
    /// The project's layer files compose, for the idle, the sword slash and the greatsword's cleave, exactly the sheet and
    /// palette map the editor bakes, for the look's own parts, for a hood over long hair with a cape and layered sleeves,
    /// for the greatsword over a long cape with gloves and the satchel, and for the longbow, each clip coloured with the
    /// table its layers give it (<see cref="CharacterComposer.MaterialsOf(IReadOnlyList{CharacterLayerClip})"/>); and each
    /// file holds what the recipe bakes for its layer, for the idle.
    /// </summary>
    [Test]
    public void ThePartsFiles_ComposeWhatTheEditorBakes()
    {
        CharacterParts parts = AssetDatabase.LoadAssetAtPath<CharacterParts>(PartsPath);
        CharacterLayer body = CharacterLayer.Read(parts.Body.bytes);
        CharacterPartPicks[] outfits =
        {
            default,
            Picks((CharacterSlot.Hair, "Long"), (CharacterSlot.Headwear, "Hood"), (CharacterSlot.Cape, "Cape"), (CharacterSlot.Top, "Layered")),
            Picks((CharacterSlot.Weapon, "Greatsword"), (CharacterSlot.Cape, "Long"), (CharacterSlot.Hands, "Gloves"), (CharacterSlot.Bag, "Satchel")),
            Picks((CharacterSlot.Weapon, "Longbow"))
        };
        CharacterComposer composer = new();
        foreach (string clipName in new[] { "Idle", "SwordSlash", "GreatswordCleave" })
        {
            int c = recipe.Clips.ToList().FindIndex(clip => clip.Name == clipName);
            CharacterClip clip = recipe.Clips[c];
            foreach (CharacterPartPicks picks in outfits)
            {
                List<CharacterLayer> worn = new();
                foreach (CharacterSlot slot in System.Enum.GetValues(typeof(CharacterSlot)))
                {
                    TextAsset file = parts.LayerOf(picks, slot);
                    worn.Add(file != null ? CharacterLayer.Read(file.bytes) : null);
                }

                baker.Outfit = picks;
                CharacterSheet sheet = baker.Bake(clip);
                baker.Outfit = default;
                Vector2Int size = clip.FrameSize;
                Color32[] pixels = new Color32[size.x * size.y];
                byte[] codes = new byte[pixels.Length];
                int wrong = 0;
                for (int direction = 0; direction < CharacterDirections.Count; direction++)
                {
                    for (int frame = 0; frame < clip.Motion.FrameCount; frame++)
                    {
                        int index = direction * clip.Motion.FrameCount + frame;
                        List<CharacterLayerFrame> layers = new() { body.Clips[c].Frames[index] };
                        layers.AddRange(worn.Select(layer => layer?.Clips[c].Frames[index]));
                        List<CharacterLayerClip> layerClips = new() { body.Clips[c] };
                        layerClips.AddRange(worn.Select(layer => layer?.Clips[c]));
                        composer.ComposeFrame(size.x, size.y, layers, CharacterComposer.MaterialsOf(layerClips), parts.InnerLineGap, pixels, codes);
                        RectInt rect = CharacterSheet.FrameRect(direction, frame, size);
                        for (int y = 0; y < size.y; y++)
                        {
                            for (int x = 0; x < size.x; x++)
                            {
                                int o = (rect.y + y) * sheet.Width + rect.x + x;
                                wrong += pixels[y * size.x + x].Equals(sheet.Pixels[o]) && codes[y * size.x + x] == sheet.PaletteMap[o] ? 0 : 1;
                            }
                        }
                    }
                }

                Assert.That(wrong, Is.Zero, $"{clipName}, outfit {picks.GetHashCode()}");
            }
        }

        IReadOnlyList<CharacterLayerKey> keys = baker.LayerKeys();
        CharacterLayerClip[] baked = baker.BakeLayers(idle, keys);
        int idleIndex = recipe.Clips.ToList().IndexOf(idle);
        for (int k = 0; k < keys.Count; k++)
        {
            CharacterLayerKey key = keys[k];
            TextAsset file = key.IsBase ? parts.Body : parts.Get((CharacterSlot)key.Slot).Options[key.Option].Layers[key.Variant];
            if (file == null)
            {
                Assert.That(baked[k].Frames.All(frame => frame.Box.width == 0), Is.True, $"key {key.Slot} {key.Option} draws nothing");
                continue;
            }

            CharacterLayerClip read = CharacterLayer.Read(file.bytes).Clips[idleIndex];
            for (int f = 0; f < read.Frames.Length; f++)
            {
                Assert.That(read.Frames[f].Box, Is.EqualTo(baked[k].Frames[f].Box), $"key {key.Slot} {key.Option} {key.Variant} frame {f}");
                Assert.That(read.Frames[f].Material, Is.EqualTo(baked[k].Frames[f].Material));
                Assert.That(read.Frames[f].Depth, Is.EqualTo(baked[k].Frames[f].Depth));
                Assert.That(read.Frames[f].Step, Is.EqualTo(baked[k].Frames[f].Step));
            }
        }
    }

    /// <summary>
    /// Each weapon's gear (Docs/PlayerCharacter.md, Phase 7), as its layer draws it: in the idle, no weapon in hand, the
    /// weapon stowed (the sword's hilt over its scabbard at the hip, the greatsword's blade and the longbow on the back)
    /// with what holds it (the scabbard, the sling, the quiver), coloured with a table of its own that gives the weapon's
    /// materials that weapon's ramps (the longbow's red grip, not the sword's); in the weapon's first attack only what
    /// stays on, no weapon, with the clip's own table; in another weapon's attack nothing; and no gear draws nothing.
    /// </summary>
    [Test]
    public void EachWeaponsGear_ShowsTheWeaponStowedOutsideItsClips_AndWhatStaysOnInThem()
    {
        CharacterClip slash = ClipNamed("SwordSlash");
        CharacterClip cleave = ClipNamed("GreatswordCleave");
        CharacterClip shot = ClipNamed("BowShot");
        (string Gear, CharacterClip Own, CharacterClip Other, CharacterMaterial Stowed, CharacterPart Holder)[] gear =
        {
            ("Sword", slash, cleave, CharacterMaterial.Guard, CharacterPart.Scabbard),
            ("Greatsword", cleave, slash, CharacterMaterial.Steel, CharacterPart.Sling),
            ("Longbow", shot, slash, CharacterMaterial.Wood, CharacterPart.Quiver)
        };
        CharacterMaterial[] weaponMaterials = { CharacterMaterial.Steel, CharacterMaterial.Guard, CharacterMaterial.Grip, CharacterMaterial.Wood, CharacterMaterial.BowString };
        foreach ((string name, CharacterClip own, CharacterClip other, CharacterMaterial stowed, CharacterPart holder) in gear)
        {
            CharacterLayerKey[] key = { new((int)CharacterSlot.Weapon, Option(CharacterSlot.Weapon, name), 0) };
            CharacterLayerClip carried = baker.BakeLayers(idle, key)[0];
            Assert.That(LayerPixels(carried, stowed, null), Is.Positive, $"{name} stowed");
            Assert.That(LayerPixels(carried, null, holder), Is.Positive, $"{name}'s {holder}");
            Assert.That(carried.Materials, Is.Not.Null, $"{name} colours its weapon");
            Assert.That(carried.Materials[(int)CharacterMaterial.Grip].Ramp, Is.EqualTo(own.Weapon.Ramp(CharacterMaterial.Grip)), $"{name}'s grip");

            CharacterLayerClip inHand = baker.BakeLayers(own, key)[0];
            Assert.That(LayerPixels(inHand, null, holder), Is.Positive, $"{name}'s {holder} stays on");
            Assert.That(weaponMaterials.Sum(material => LayerPixels(inHand, material, null)), Is.Zero, $"{name} is in hand, not stowed");
            Assert.That(inHand.Materials, Is.Null, $"{name}'s own clip keeps its table");
            Assert.That(baker.BakeLayers(other, key)[0].Frames.All(frame => frame.Box.width == 0), Is.True, $"{name} in another weapon's clip");
        }

        Assert.That(recipe.Weapons[0].Weapon, Is.EqualTo(slash.Weapon), "the look's own gear is the sword's, the starting weapon");
        CharacterLayerKey[] none = { new((int)CharacterSlot.Weapon, Option(CharacterSlot.Weapon, "None"), 0) };
        foreach (CharacterClip clip in new[] { idle, slash })
        {
            Assert.That(baker.BakeLayers(clip, none)[0].Frames.All(frame => frame.Box.width == 0), Is.True, $"no gear in {clip.Name}");
        }
    }

    /// <summary>
    /// A weapon slung on the back shows over a cape (Docs/PlayerCharacter.md, Phase 7): facing north, the greatsword's
    /// blade and the longbow show about as much over a long cape as over the bare back, the cape showing around them.
    /// </summary>
    [Test]
    public void AWeaponOnTheBack_ShowsOverACape()
    {
        foreach ((string name, CharacterMaterial material) in new[] { ("Greatsword", CharacterMaterial.Steel), ("Longbow", CharacterMaterial.Wood) })
        {
            baker.Outfit = Picks((CharacterSlot.Weapon, name));
            int bare = baker.Render(idle, North, 0).Count(material);
            baker.Outfit = Picks((CharacterSlot.Weapon, name), (CharacterSlot.Cape, "Long"));
            CharacterCanvas caped = baker.Render(idle, North, 0);
            baker.Outfit = default;
            Assert.That(bare, Is.Positive, name);
            Assert.That(caped.Count(material), Is.GreaterThanOrEqualTo(bare * 3 / 4), $"{name} over the cape");
            Assert.That(caped.Count(CharacterMaterial.Cloak), Is.Positive, $"the cape around the {name}");
        }
    }

    /// <summary>
    /// The clip of the recipe named <paramref name="clipName"/>.
    /// </summary>
    private CharacterClip ClipNamed(string clipName)
    {
        return recipe.Clips.First(clip => clip.Name == clipName);
    }

    /// <summary>
    /// The index of the recipe's option of <paramref name="slot"/> named <paramref name="option"/>.
    /// </summary>
    private int Option(CharacterSlot slot, string option)
    {
        return Enumerable.Range(0, recipe.PartOptions(slot)).First(o => recipe.PartName(slot, o) == option);
    }

    /// <summary>
    /// How many pixels of <paramref name="layer"/>'s frames are, when given, <paramref name="material"/> and of
    /// <paramref name="part"/>.
    /// </summary>
    private static int LayerPixels(CharacterLayerClip layer, CharacterMaterial? material, CharacterPart? part)
    {
        int count = 0;
        foreach (CharacterLayerFrame frame in layer.Frames)
        {
            for (int i = 0; i < frame.Material.Length; i++)
            {
                if (frame.Material[i] != CharacterLayer.Empty && (material == null || frame.Material[i] == (byte)material) && (part == null || frame.Part[i] == (byte)part))
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// The idle drawn in one option of <paramref name="slot"/>, named <paramref name="option"/>, facing
    /// <paramref name="direction"/>; the rest of the outfit the recipe's own.
    /// </summary>
    private CharacterCanvas Outfit(CharacterSlot slot, string option, int direction)
    {
        baker.Outfit = Picks((slot, option));
        CharacterCanvas canvas = baker.Render(idle, direction, 0);
        baker.Outfit = default;
        return canvas;
    }

    /// <summary>
    /// Picks of the recipe's options named in <paramref name="named"/>, the first of every other slot.
    /// </summary>
    private CharacterPartPicks Picks(params (CharacterSlot Slot, string Option)[] named)
    {
        CharacterPartPicks picks = default;
        foreach ((CharacterSlot slot, string option) in named)
        {
            int index = Enumerable.Range(0, recipe.PartOptions(slot)).First(o => recipe.PartName(slot, o) == option);
            picks = picks.With(slot, index);
        }

        return picks;
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
    /// every frame whose pose opens the mouth the rows below its line are the inside of the mouth, dark, so a yawn reads
    /// at this size: a material of its own, not the brows', which a player's colours give the hair's.
    /// </summary>
    [Test]
    public void TheYawn_ShutsTheEyes_AndOpensTheMouth()
    {
        CharacterClip yawn = recipe.Clips.First(clip => clip.Name == "IdleYawn");
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
                Assert.That(canvas.Count(CharacterMaterial.MouthInside), Is.GreaterThanOrEqualTo(mouthWidth), $"frame {frame}");
            }
        }

        Assert.That(shut, Is.GreaterThan(0));
        Assert.That(open, Is.GreaterThan(0));
    }

    /// <summary>
    /// Every pixel of every sheet whose palette map names a dye and tone (Docs/PlayerCharacter.md, Phase 5) is that tone
    /// of the ramp the recipe gives the dye's material, one material a dye, so a palette of the recipe's own ramps draws
    /// every sheet exactly; and on the slash's contact frame, the bolt's release and the yawn, every drawn pixel names the
    /// dye of its material, and none for the sword, the cast's light and the inside of the open mouth, whose colours
    /// stay.
    /// </summary>
    [Test]
    public void ThePaletteMaps_NameTheDyeAndToneOfEveryPixelAPlayerColours()
    {
        Dictionary<CharacterDye, CharacterMaterial> materialOf = new();
        for (int m = 0; m < CharacterMaterials.Count; m++)
        {
            CharacterDye dye = recipe.DyeOf((CharacterMaterial)m);
            if (dye != CharacterDye.None)
            {
                materialOf.Add(dye, (CharacterMaterial)m);
            }
        }

        int dyed = 0;
        int wrong = 0;
        foreach (CharacterSheet sheet in Sheets.Values)
        {
            Assert.That(sheet.PaletteMap, Is.Not.Null, sheet.Clip.Name);
            for (int i = 0; i < sheet.PaletteMap.Length; i++)
            {
                byte code = sheet.PaletteMap[i];
                if (code == 0)
                {
                    continue;
                }

                dyed++;
                bool known = materialOf.TryGetValue(CharacterPaletteMap.DyeOf(code), out CharacterMaterial material);
                wrong += known && recipe.Ramp(material).Tone(CharacterPaletteMap.ToneOf(code)).Equals(sheet.Pixels[i]) ? 0 : 1;
            }
        }

        Assert.That(dyed, Is.GreaterThan(0));
        Assert.That(wrong, Is.Zero, $"of {dyed} dyed pixels");

        CharacterClip slash = recipe.Clips.First(clip => clip.Name == "SwordSlash");
        CharacterClip bolt = recipe.Clips.First(clip => clip.Name == "CastBolt");
        CharacterClip yawn = recipe.Clips.First(clip => clip.Name == "IdleYawn");
        Assert.That(KeptPixels(slash, East, slash.Motion.ContactFrame, CharacterMaterials.IsWeapons), Is.Positive, "the sword");
        Assert.That(KeptPixels(bolt, South, bolt.Motion.ContactFrame, CharacterMaterials.IsLight), Is.Positive, "the cast's light");
        int mouth = 0;
        for (int frame = 0; frame < yawn.Motion.FrameCount; frame++)
        {
            mouth += KeptPixels(yawn, South, frame, material => material == CharacterMaterial.MouthInside);
        }

        Assert.That(mouth, Is.Positive, "the inside of the mouth");
    }

    /// <summary>
    /// The project's animation set names the dyes its sheets' palette maps carry: from the skin to the buckle, not the
    /// blush, the gloves, the strap or the pouch its own outfit does not draw (the sword's scabbard is the belt's
    /// leather and its fittings the buckle's metal); an adventurer drawn with them carries theirs too.
    /// </summary>
    [Test]
    public void TheAnimationSet_NamesTheDyesItsSheetsCarry()
    {
        CharacterAnimationSet set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(CharacterSheetWriter.SetPath(recipe));
        Assert.That(set.Dyes, Is.EqualTo(Sheets.Values.Aggregate(0, (dyes, sheet) => dyes | sheet.Dyes)));
        CharacterDye[] carried =
        {
            CharacterDye.Skin, CharacterDye.Mouth, CharacterDye.Hair, CharacterDye.Brows, CharacterDye.Eyes, CharacterDye.EyeGlint,
            CharacterDye.Tunic, CharacterDye.Shirt, CharacterDye.Trousers, CharacterDye.Boots, CharacterDye.Belt, CharacterDye.Buckle
        };
        foreach (CharacterDye dye in System.Enum.GetValues(typeof(CharacterDye)))
        {
            Assert.That(set.Carries(dye), Is.EqualTo(carried.Contains(dye)), dye.ToString());
        }

        CharacterBaker dressedBaker = new(Copy("{\"blush\":{\"x\":24,\"y\":-20}}")) { Outfit = Picks((CharacterSlot.Hands, "Gloves"), (CharacterSlot.Bag, "Satchel")) };
        CharacterSheet dressed = dressedBaker.Bake(idle);
        foreach (CharacterDye dye in new[] { CharacterDye.Gloves, CharacterDye.Strap, CharacterDye.Pouch, CharacterDye.Blush })
        {
            Assert.That(dressed.Dyes & (1 << (int)dye), Is.Not.Zero, dye.ToString());
        }
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
    /// How many drawn pixels of <paramref name="clip"/>'s frame <paramref name="frame"/> facing
    /// <paramref name="direction"/> are of a material <paramref name="kept"/> accepts and name no dye in its palette map;
    /// asserts that every drawn pixel names the dye of its material.
    /// </summary>
    private int KeptPixels(CharacterClip clip, int direction, int frame, System.Func<CharacterMaterial, bool> kept)
    {
        CharacterCanvas canvas = baker.Render(clip, direction, frame);
        byte[] codes = baker.PaletteMap(canvas);
        int count = 0;
        int wrong = 0;
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                if (!canvas.IsFilled(x, y))
                {
                    continue;
                }

                CharacterMaterial material = canvas.MaterialAt(x, y);
                byte code = codes[y * canvas.Width + x];
                CharacterDye named = code == 0 ? CharacterDye.None : CharacterPaletteMap.DyeOf(code);
                wrong += named == recipe.DyeOf(material) ? 0 : 1;
                count += kept(material) && code == 0 ? 1 : 0;
            }
        }

        Assert.That(wrong, Is.Zero, $"{clip.Name} {direction} {frame}");
        return count;
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
