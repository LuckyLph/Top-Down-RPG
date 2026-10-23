using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Tests for characters drawn by hand in Aseprite (Docs/AsepriteCharacters.md, Phase 1), on files written by
/// <see cref="AsepriteFileWriter"/>: a clip's file gives its frames laid out as the baker's sheets, its durations and its
/// contact frame; layers draw as Aseprite draws them (normal blending at the cel's and layer's opacity, hidden layers
/// and hidden groups left out, linked cels and their user data followed); every validation rule reports a broken file
/// naming the file, tag and frame; the import settings are enforced under Assets/Art/Characters only; the recipe
/// refuses what it cannot write; the inspector reads a clip again when its file changes; saving files regenerates the
/// recipe using them, once, and no other; the clips a prefab plays are listed; the rogue's baked clips written as files
/// and generated give sheets byte for byte the baked ones and a set equal to the baked one, and generating again writes
/// nothing; and generating after a frame is added, the directions are moved within the file, a layer is added or the
/// canvas is resized keeps the ID of every slice still drawn. Imported files and generated sheets live in folders of
/// their own, deleted after each test.
/// </summary>
public class AsepriteCharacterTests
{
    private const string Character = "AsepriteTest";
    private const string OtherCharacter = "AsepriteOther";
    private const string ArtFolder = AsepriteCharacterRecipe.ArtRoot + "/" + Character;
    private const string OutsideFolder = "Assets/Tests/AsepriteOutside";
    private const string RecipePath = "Assets/Settings/Characters/Recipe_Rogue.asset";
    private const string RogueSetPath = "Assets/Sprites/Characters/Rogue/Rogue_AnimationSet.asset";
    private const string PlayerPath = "Assets/Prefabs/Player/Player.prefab";
    private const string WeaselPath = "Assets/Prefabs/Mobs/Weasel.prefab";
    private const string FileName = "Bandit_Run.aseprite";
    private static readonly Vector2Int SmallFeet = new(3, 1);

    private readonly List<string> tempFolders = new();
    private readonly List<Object> created = new();
    private string createdArtFolder;

    /// <summary>
    /// Destroys the objects the test made and deletes its files, imported files, sheets and set.
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
        foreach (string folder in tempFolders)
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        tempFolders.Clear();
        AssetDatabase.DeleteAsset(CharacterSheetWriter.FolderOf(Character));
        AssetDatabase.DeleteAsset(CharacterSheetWriter.FolderOf(OtherCharacter));
        AssetDatabase.DeleteAsset(OutsideFolder);
        if (createdArtFolder != null)
        {
            AssetDatabase.DeleteAsset(createdArtFolder);
            createdArtFolder = null;
        }
    }

    /// <summary>
    /// An attack's file gives its frames on a sheet laid out as the baker's (a row per direction, east on top), its
    /// first direction's durations and its contact frame; the same file read as a gait refuses the marks, and an
    /// unmarked one reads as a gait without a contact frame.
    /// </summary>
    [Test]
    public void Read_AWellFormedClip_GivesItsSheetDurationsAndContact()
    {
        string path = WriteTemp(Clip(millisecondsOf: (d, f) => f == 0 ? 120 : 45, marked: (d, f) => f == 1));

        AsepriteClipFrames frames = AsepriteClipReader.Read(path, CharacterGait.None, SmallFeet);

        Assert.That(frames.Problems, Is.Empty);
        Assert.That(frames.FrameSize, Is.EqualTo(new Vector2Int(8, 8)));
        Assert.That(frames.FrameCount, Is.EqualTo(2));
        Assert.That(frames.FrameSeconds, Is.EqualTo(new[] { 0.12f, 0.045f }));
        Assert.That(frames.ContactFrame, Is.EqualTo(1));
        Assert.That(frames.Pixels.Length, Is.EqualTo(16 * 64));
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            for (int frame = 0; frame < 2; frame++)
            {
                Assert.That(SheetPixel(frames, direction, frame, 2, 1), Is.EqualTo(Colour(direction, frame)), $"{direction} {frame}");
                Assert.That(SheetPixel(frames, direction, frame, 4, 6), Is.EqualTo(Colour(direction, frame)), $"{direction} {frame}");
                Assert.That(SheetPixel(frames, direction, frame, 0, 0), Is.EqualTo(new Color32(0, 0, 0, 0)), $"{direction} {frame}");
                Assert.That(SheetPixel(frames, direction, frame, 5, 7), Is.EqualTo(new Color32(0, 0, 0, 0)), $"{direction} {frame}");
            }
        }

        Assert.That(AsepriteClipReader.Read(path, CharacterGait.Run, SmallFeet).IsValid, Is.False);
        AsepriteClipFrames gait = AsepriteClipReader.Read(WriteTemp(Clip()), CharacterGait.Run, SmallFeet);
        Assert.That(gait.Problems, Is.Empty);
        Assert.That(gait.ContactFrame, Is.EqualTo(-1));
    }

    /// <summary>
    /// A frame is its visible layers drawn in order with Aseprite's normal blending at the cel's opacity times the
    /// layer's; a hidden layer and the layers of a hidden group are left out, a visible group's layers drawn; linked
    /// cels draw the cel they link to and carry its user data, here a contact mark on a hidden layer.
    /// </summary>
    [Test]
    public void Read_LayeredFrames_DrawAsAsepriteDraws()
    {
        AsepriteFileWriter writer = new(4, 2);
        int under = writer.AddLayer("Under");
        int over = writer.AddLayer("Over", opacity: 128);
        int guide = writer.AddLayer("Guide", false);
        int sketch = writer.AddGroup("Sketch", false);
        int lines = writer.AddLayer("Lines", group: sketch);
        int gear = writer.AddGroup("Gear");
        int belt = writer.AddLayer("Belt", group: gear);
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            int frame = writer.AddFrame(100);
            writer.AddTag(CharacterDirections.Name(direction), frame, frame);
        }

        writer.SetCel(under, 0, new RectInt(0, 0, 1, 1), new[] { new Color32(200, 100, 0, 255) });
        writer.SetCel(over, 0, new RectInt(0, 0, 2, 1), new[] { new Color32(0, 0, 255, 255), new Color32(10, 20, 30, 200) });
        writer.SetCel(guide, 0, new RectInt(2, 0, 1, 1), new[] { new Color32(255, 0, 0, 255) }, AsepriteFileWriter.ContactMarker);
        writer.SetCel(lines, 0, new RectInt(3, 0, 1, 1), new[] { new Color32(0, 255, 0, 255) });
        writer.SetCel(belt, 0, new RectInt(2, 1, 1, 1), new[] { new Color32(5, 6, 7, 255) }, opacity: 100);
        foreach (int layer in new[] { under, over, guide, lines, belt })
        {
            for (int frame = 1; frame < CharacterDirections.Count; frame++)
            {
                writer.SetLinkedCel(layer, frame, 0);
            }
        }

        AsepriteClipFrames frames = AsepriteClipReader.Read(WriteTemp(writer), CharacterGait.None, new Vector2Int(1, 0));

        Assert.That(frames.Problems, Is.Empty);
        Assert.That(frames.ContactFrame, Is.EqualTo(0));
        Color32 clear = new(0, 0, 0, 0);
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            Assert.That(SheetPixel(frames, direction, 0, 0, 0), Is.EqualTo(new Color32(100, 50, 128, 255)), $"{direction}: over on under");
            Assert.That(SheetPixel(frames, direction, 0, 1, 0), Is.EqualTo(new Color32(10, 20, 30, 100)), $"{direction}: over alone");
            Assert.That(SheetPixel(frames, direction, 0, 2, 0), Is.EqualTo(clear), $"{direction}: hidden layer");
            Assert.That(SheetPixel(frames, direction, 0, 3, 0), Is.EqualTo(clear), $"{direction}: hidden group");
            Assert.That(SheetPixel(frames, direction, 0, 2, 1), Is.EqualTo(new Color32(5, 6, 7, 100)), $"{direction}: visible group, cel opacity");
            Assert.That(SheetPixel(frames, direction, 0, 0, 1), Is.EqualTo(clear), $"{direction}: nothing drawn");
        }
    }

    /// <summary>
    /// Each validation rule reports a broken file, naming the file and, where it has them, the tag and the frame, and
    /// gives no sheet.
    /// </summary>
    [TestCase("no tag", "Bandit_Run.aseprite: no tag N;")]
    [TestCase("tag twice", "Bandit_Run.aseprite: 2 tags named E.")]
    [TestCase("tags overlap", "Bandit_Run.aseprite: tags E and NE overlap.")]
    [TestCase("tag reversed", "Bandit_Run.aseprite, tag W: plays Reverse; direction tags play forward.")]
    [TestCase("lengths differ", "Bandit_Run.aseprite, tag NW: 3 frames, but 2 in tag E.")]
    [TestCase("durations differ", "Bandit_Run.aseprite, tag SW, frame 2 (Aseprite frame 12): 60 ms, but 100 ms in tag E.")]
    [TestCase("gait marked", "Bandit_Run.aseprite, tag E, frame 1 (Aseprite frame 1): marked contact, but a run has no contact frame.")]
    [TestCase("action unmarked", "Bandit_Run.aseprite, tag W: no frame is marked contact;")]
    [TestCase("action marked twice", "Bandit_Run.aseprite, tag NE: frames 1 and 2 are each marked contact; mark one.")]
    [TestCase("contact moves", "Bandit_Run.aseprite, tag S: frame 1 is marked contact, but frame 2 in tag E.")]
    [TestCase("frame empty", "Bandit_Run.aseprite, tag SE, frame 2 (Aseprite frame 16): empty.")]
    [TestCase("past the canvas", "Bandit_Run.aseprite, tag E, frame 1 (Aseprite frame 1): layer Body draws past the canvas, at (8, 1).")]
    [TestCase("blend mode", "Bandit_Run.aseprite, layer Shade: blends by Multiply; only normal layers are drawn.")]
    [TestCase("group opacity", "Bandit_Run.aseprite, group Gear: blends by Normal at opacity 128; a group must blend normally at full opacity.")]
    [TestCase("feet outside", "Bandit_Run.aseprite: the feet pixel (8, 0) lies outside its 8 x 8 canvas.")]
    [TestCase("not a file", "Bandit_Run.aseprite: could not be read as an Aseprite file.")]
    public void Read_ABrokenClip_ReportsWhereItIsBroken(string broken, string expected)
    {
        CharacterGait gait = CharacterGait.None;
        Vector2Int feet = SmallFeet;
        Func<int, int, bool> eachRelease = (d, f) => f == 1;
        AsepriteFileWriter writer = null;
        switch (broken)
        {
            case "no tag":
                writer = Clip(marked: eachRelease, tagOf: d => d == 2 ? null : CharacterDirections.Name(d));
                break;
            case "tag twice":
                writer = Clip(marked: eachRelease, tagOf: d => d == 2 ? "E" : CharacterDirections.Name(d));
                break;
            case "tags overlap":
                writer = Clip(marked: eachRelease, tagOf: d => d == 1 ? null : CharacterDirections.Name(d));
                writer.AddTag("NE", 1, 3);
                break;
            case "tag reversed":
                writer = Clip(marked: eachRelease, loopOf: d => d == 4 ? AsepriteFileWriter.LoopReverse : AsepriteFileWriter.LoopForward);
                break;
            case "lengths differ":
                writer = Clip(marked: eachRelease, framesIn: d => d == 3 ? 3 : 2);
                break;
            case "durations differ":
                writer = Clip(marked: eachRelease, millisecondsOf: (d, f) => d == 5 && f == 1 ? 60 : 100);
                break;
            case "gait marked":
                gait = CharacterGait.Run;
                writer = Clip(marked: (d, f) => d == 0 && f == 0);
                break;
            case "action unmarked":
                writer = Clip(marked: (d, f) => d != 4 && f == 1);
                break;
            case "action marked twice":
                writer = Clip(marked: (d, f) => f == 1 || d == 1);
                break;
            case "contact moves":
                writer = Clip(marked: (d, f) => d == 6 ? f == 0 : f == 1);
                break;
            case "frame empty":
                writer = Clip(marked: eachRelease, empty: (d, f) => d == 7 && f == 1);
                break;
            case "past the canvas":
                writer = Clip(marked: eachRelease);
                writer.SetCel(0, 0, new RectInt(6, 1, 3, 2), Enumerable.Repeat(Colour(0, 0), 6).ToArray());
                break;
            case "blend mode":
                writer = Clip(marked: eachRelease);
                writer.SetCel(writer.AddLayer("Shade", blendMode: AsepriteFileWriter.BlendMultiply), 0, new RectInt(2, 2, 1, 1), new[] { Colour(1, 1) });
                break;
            case "group opacity":
                writer = Clip(marked: eachRelease);
                writer.SetCel(writer.AddLayer("Belt", group: writer.AddGroup("Gear", opacity: 128)), 0, new RectInt(2, 2, 1, 1), new[] { Colour(1, 1) });
                break;
            case "feet outside":
                writer = Clip(marked: eachRelease);
                feet = new Vector2Int(8, 0);
                break;
        }

        string path;
        if (writer != null)
        {
            path = WriteTemp(writer);
        }
        else
        {
            path = TempPath();
            File.WriteAllBytes(path, Enumerable.Repeat((byte)7, 200).ToArray());
        }

        AsepriteClipFrames frames = AsepriteClipReader.Read(path, gait, feet);

        Assert.That(frames.IsValid, Is.False);
        Assert.That(frames.Pixels, Is.Null);
        Assert.That(frames.Problems.Any(p => p.StartsWith(expected, StringComparison.Ordinal)), Is.True, $"{expected}\nnot in:\n{string.Join("\n", frames.Problems)}");
    }

    /// <summary>
    /// A file imported under Assets/Art/Characters gets the enforced settings, so the importer makes only a point
    /// filtered texture without mipmaps and its sprites at 32 px per unit, no prefab, animation clips or controller;
    /// the same file elsewhere keeps the importer's defaults.
    /// </summary>
    [Test]
    public void ImportSettings_UnderArtCharacters_KeepTheImportersOutputLight()
    {
        Object file = Import(Clip(), "Idle");
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(file));

        Assert.That(assets.OfType<GameObject>(), Is.Empty);
        Assert.That(assets.OfType<AnimationClip>(), Is.Empty);
        Assert.That(assets.OfType<RuntimeAnimatorController>(), Is.Empty);
        Texture2D texture = assets.OfType<Texture2D>().Single();
        Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Point));
        Assert.That(texture.mipmapCount, Is.EqualTo(1));
        Sprite[] sprites = assets.OfType<Sprite>().ToArray();
        Assert.That(sprites.Length, Is.EqualTo(16));
        Assert.That(sprites.All(s => Mathf.Approximately(s.pixelsPerUnit, CharacterSheetWriter.PixelsPerUnit)), Is.True);

        Object outside = ImportOutside(Clip(), $"{Character}_Idle");
        Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(outside)).OfType<GameObject>(), Is.Not.Empty);
    }

    /// <summary>
    /// The recipe generates only with a name of letters and digits and clips each with an .aseprite file under
    /// Assets/Art/Characters named after the character and the clip, listed once, a moving gait with its natural
    /// speed.
    /// </summary>
    [Test]
    public void CanGenerate_RefusesARecipeItCannotWrite()
    {
        Object run = Import(Clip(), "Run");
        Object outside = ImportOutside(Clip(), $"{Character}_Walk");
        AsepriteClip runClip = new(run, CharacterGait.Run, false, 4f, SmallFeet);

        Assert.That(Recipe("Bad Name", runClip).CanGenerate(out string problem), Is.False);
        StringAssert.Contains("letters and digits", problem);
        Assert.That(Recipe(Character).CanGenerate(out problem), Is.False);
        StringAssert.Contains("no clips", problem);
        Assert.That(Recipe(Character, new AsepriteClip()).CanGenerate(out problem), Is.False);
        StringAssert.Contains("clip 1 needs an .aseprite file", problem);
        Assert.That(Recipe("Bandit", runClip).CanGenerate(out problem), Is.False);
        StringAssert.Contains("named Bandit_<clip>.aseprite", problem);
        Assert.That(Recipe(Character, new AsepriteClip(outside, CharacterGait.Walk, false, 1f, SmallFeet)).CanGenerate(out problem), Is.False);
        StringAssert.Contains("must be under Assets/Art/Characters", problem);
        Assert.That(Recipe(Character, runClip, runClip).CanGenerate(out problem), Is.False);
        StringAssert.Contains("clip Run is listed twice", problem);
        Assert.That(Recipe(Character, new AsepriteClip(run, CharacterGait.Run, false, 0f, SmallFeet)).CanGenerate(out problem), Is.False);
        StringAssert.Contains("clip Run is a run and needs its natural speed", problem);

        AsepriteCharacterRecipe recipe = Recipe(Character, runClip);
        Assert.That(recipe.CanGenerate(out problem), Is.True, problem);
        Assert.That(recipe.ClipName(runClip), Is.EqualTo("Run"));
    }

    /// <summary>
    /// The inspector reads the picked clip from its file, and reads it again when the file changes: new durations,
    /// then a problem.
    /// </summary>
    [Test]
    public void ThePreview_ReadsTheClipAgainWhenItsFileChanges()
    {
        Object file = Import(Clip(), "Run");
        string path = AssetDatabase.GetAssetPath(file);
        AsepriteCharacterRecipe recipe = Recipe(Character, new AsepriteClip(file, CharacterGait.Run, false, 4f, SmallFeet));
        AsepriteCharacterRecipeEditor inspector = (AsepriteCharacterRecipeEditor)Editor.CreateEditor(recipe);
        created.Add(inspector);

        inspector.RebuildIfChanged(recipe);
        Assert.That(inspector.PreviewProblem, Is.Null);
        Assert.That(inspector.PreviewFrames.FrameSeconds, Is.EqualTo(new[] { 0.1f, 0.1f }));

        Clip(millisecondsOf: (d, f) => 70).Write(path);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(1));
        inspector.RebuildIfChanged(recipe);
        Assert.That(inspector.PreviewProblem, Is.Null);
        Assert.That(inspector.PreviewFrames.FrameSeconds, Is.EqualTo(new[] { 0.07f, 0.07f }));

        Clip(marked: (d, f) => f == 0).Write(path);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(2));
        inspector.RebuildIfChanged(recipe);
        StringAssert.Contains("marked contact, but a run has no contact frame", inspector.PreviewProblem);
    }

    /// <summary>
    /// Saving two files of a character in one import queues its recipe once; running the queue regenerates that recipe
    /// alone, its set taking the files' new durations, while another character's recipe, whose file was not saved, is
    /// neither queued nor rewritten.
    /// </summary>
    [Test]
    public void SavingFiles_RegeneratesTheRecipeUsingThem_Once()
    {
        AsepriteRegeneration.Flush();
        Object run = Import(Clip(), "Run");
        Object walk = Import(Clip(), "Walk");
        Object other = ImportAs(Clip(), $"{OtherCharacter}_Run");
        string recipePath = $"{ArtFolder}/{Character}_Recipe.asset";
        AsepriteCharacterRecipe recipe = AsepriteCharacterRecipe.Create(Character, null, new[] { new AsepriteClip(run, CharacterGait.Run, false, 4f, SmallFeet), new AsepriteClip(walk, CharacterGait.Walk, false, 2f, SmallFeet) });
        AssetDatabase.CreateAsset(recipe, recipePath);
        AsepriteCharacterRecipe otherRecipe = AsepriteCharacterRecipe.Create(OtherCharacter, null, new[] { new AsepriteClip(other, CharacterGait.Run, false, 4f, SmallFeet) });
        AssetDatabase.CreateAsset(otherRecipe, $"{ArtFolder}/{OtherCharacter}_Recipe.asset");
        Assert.That(AsepriteCharacterWriter.Generate(recipe).Problems, Is.Empty);
        Assert.That(AsepriteCharacterWriter.Generate(otherRecipe).Problems, Is.Empty);
        AsepriteRegeneration.Flush();
        string otherSheet = CharacterSheetWriter.SheetPath(OtherCharacter, "Run");
        DateTime otherWritten = File.GetLastWriteTimeUtc(otherSheet);
        string otherSet = CharacterSheetWriter.SetPath(OtherCharacter);
        DateTime otherSetWritten = File.GetLastWriteTimeUtc(otherSet);

        AssetDatabase.StartAssetEditing();
        try
        {
            Clip(millisecondsOf: (d, f) => 70).Write(AssetDatabase.GetAssetPath(run));
            Clip(millisecondsOf: (d, f) => 60).Write(AssetDatabase.GetAssetPath(walk));
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(run), ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(walk), ImportAssetOptions.ForceUpdate);
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        Assert.That(AsepriteRegeneration.Queued, Is.EqualTo(new[] { recipePath }));
        Assert.That(AsepriteRegeneration.Flush(), Is.EqualTo(new[] { recipePath }));
        Assert.That(AsepriteRegeneration.Queued, Is.Empty);
        CharacterAnimationSet set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(CharacterSheetWriter.SetPath(Character));
        Assert.That(set.Find("Run").FrameSeconds(0), Is.EqualTo(0.07f));
        Assert.That(set.Find("Walk").FrameSeconds(0), Is.EqualTo(0.06f));
        Assert.That(File.GetLastWriteTimeUtc(otherSheet), Is.EqualTo(otherWritten));
        Assert.That(File.GetLastWriteTimeUtc(otherSet), Is.EqualTo(otherSetWritten));
    }

    /// <summary>
    /// The player plays its class's four casts and, for each of its three weapons, the attack and two casts; the
    /// Weasel plays its dagger slash; the clips a set lacks are listed with what plays them.
    /// </summary>
    [Test]
    public void ClipNeeds_OfThePlayerAndTheWeasel_NameEveryClipTheyPlay()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        GameObject weasel = AssetDatabase.LoadAssetAtPath<GameObject>(WeaselPath);

        Assert.That(CharacterClipNeeds.Of(player).Select(n => n.Clip), Is.EquivalentTo(new[]
        {
            "CastBolt", "CastLeap", "CastMend", "CastSmite", "SwordSlash", "CastWhirl", "CastLunge", "GreatswordCleave", "CastSweep", "CastCharge", "BowShot", "CastVolley", "CastRoll"
        }));
        Assert.That(CharacterClipNeeds.Of(weasel).Select(n => n.Clip), Is.EqualTo(new[] { "DaggerSlash" }));
        Assert.That(CharacterClipNeeds.Of(null), Is.Empty);

        IReadOnlyList<string> missing = CharacterClipNeeds.Missing(player, new[] { "Run", "SwordSlash", "CastBolt" });
        Assert.That(missing.Count, Is.EqualTo(11));
        Assert.That(missing.Any(m => m.Contains("plays GreatswordCleave for the weapon Greatsword's attack")), Is.True, string.Join("\n", missing));
        Assert.That(CharacterClipNeeds.Missing(weasel, new[] { "DaggerSlash" }), Is.Empty);
    }

    /// <summary>
    /// The rogue's baked clips, the casts over the run excepted, written as files by the Phase 0 helper and generated
    /// for the player give sheets byte for byte the baked ones and a set equal to the baked one clip by clip (names,
    /// gaits, armed, frame counts, durations to the millisecond, contact frames, natural speeds, every sprite's rect and
    /// pivot), with no problem and nothing the player plays missing; generating again writes nothing.
    /// </summary>
    [Test]
    public void Generate_TheRoguesClipsAsFiles_GivesTheBakedSheetsAndSet()
    {
        CharacterRecipe rogue = AssetDatabase.LoadAssetAtPath<CharacterRecipe>(RecipePath);
        CharacterAnimationSet baked = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(RogueSetPath);
        List<CharacterClip> rogueClips = rogue.Clips.Where(c => c.Motion.GaitFrames == 0).ToList();
        Assert.That(rogueClips.Count, Is.EqualTo(17));
        List<string> paths = new();
        EnsureArtFolder();
        foreach (CharacterClip clip in rogueClips)
        {
            string path = $"{ArtFolder}/{Character}_{clip.Name}.aseprite";
            AsepriteFileWriter.FromSheet(BakedSheet(rogue, clip)).Write(path);
            paths.Add(path);
        }

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string path in paths)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        List<AsepriteClip> clips = new();
        for (int i = 0; i < rogueClips.Count; i++)
        {
            CharacterAnimationClip bakedClip = baked.Find(rogueClips[i].Name);
            clips.Add(new AsepriteClip(AssetDatabase.LoadMainAssetAtPath(paths[i]), bakedClip.Gait, bakedClip.Armed, bakedClip.NaturalSpeed, rogueClips[i].Pivot));
        }

        AsepriteCharacterRecipe recipe = AsepriteCharacterRecipe.Create(Character, AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath), clips);
        created.Add(recipe);

        AsepriteCharacterReport report = AsepriteCharacterWriter.Generate(recipe);

        Assert.That(report.Problems, Is.Empty);
        CharacterAnimationSet set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(CharacterSheetWriter.SetPath(Character));
        Assert.That(set.Clips.Select(c => c.Name), Is.EqualTo(rogueClips.Select(c => c.Name)));
        foreach (CharacterClip clip in rogueClips)
        {
            string name = clip.Name;
            Assert.That(File.ReadAllBytes(CharacterSheetWriter.SheetPath(Character, name)), Is.EqualTo(File.ReadAllBytes(CharacterSheetWriter.SheetPath(rogue, clip))), name);
            CharacterAnimationClip expected = baked.Find(name);
            CharacterAnimationClip actual = set.Find(name);
            Assert.That((actual.Gait, actual.Armed, actual.FrameCount, actual.ContactFrame, actual.GaitFrames), Is.EqualTo((expected.Gait, expected.Armed, expected.FrameCount, expected.ContactFrame, expected.GaitFrames)), name);
            Assert.That(actual.NaturalSpeed, Is.EqualTo(expected.NaturalSpeed), name);
            Assert.That(actual.IsComplete, Is.True, name);
            for (int frame = 0; frame < expected.FrameCount; frame++)
            {
                Assert.That(actual.FrameSeconds(frame), Is.EqualTo(expected.FrameSeconds(frame)).Within(0.0005f), $"{name} {frame}");
                for (int direction = 0; direction < CharacterDirections.Count; direction++)
                {
                    Sprite sprite = actual.SpriteAt(direction, frame);
                    Sprite bakedSprite = expected.SpriteAt(direction, frame);
                    string where = $"{name} {CharacterDirections.Name(direction)} {frame}";
                    Assert.That(sprite.name, Is.EqualTo(CharacterSheetWriter.SliceName(Character, name, direction, frame)), where);
                    Assert.That(sprite.rect, Is.EqualTo(bakedSprite.rect), where);
                    Assert.That(sprite.pivot, Is.EqualTo(bakedSprite.pivot), where);
                    Assert.That(sprite.pixelsPerUnit, Is.EqualTo(bakedSprite.pixelsPerUnit), where);
                }
            }
        }

        AsepriteCharacterReport again = AsepriteCharacterWriter.Generate(recipe);
        Assert.That(again.Written, Is.Empty);
        Assert.That(again.Problems, Is.Empty);
    }

    /// <summary>
    /// The rogue's run generated from its file, for the player, reports every clip the player plays and lacks; then
    /// after a frame is added in every direction, the directions are moved within the file, a layer is added and the
    /// canvas is grown 8 px on every side (the feet pixel moved with it), generating again keeps the sprite ID of every
    /// slice still drawn, and the set points at the sheet's slices. Directions moved within the file give the baked
    /// sheet again.
    /// </summary>
    [Test]
    public void Generate_AfterTheFileIsEdited_KeepsEverySliceStillDrawn()
    {
        CharacterRecipe rogue = AssetDatabase.LoadAssetAtPath<CharacterRecipe>(RecipePath);
        CharacterClip run = rogue.Clips.Single(c => c.Name == "Run");
        CharacterSheet sheet = BakedSheet(rogue, run);
        float naturalSpeed = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(RogueSetPath).Find("Run").NaturalSpeed;
        string sheetPath = CharacterSheetWriter.SheetPath(Character, "Run");
        Object file = Import(RunFile(sheet), "Run");

        AsepriteCharacterReport first = AsepriteCharacterWriter.Generate(Recipe(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath), Character, new AsepriteClip(file, CharacterGait.Run, false, naturalSpeed, run.Pivot)));
        Assert.That(first.Problems.Count, Is.EqualTo(13), string.Join("\n", first.Problems));
        Assert.That(first.Problems.All(p => p.StartsWith("Player plays ", StringComparison.Ordinal)), Is.True, string.Join("\n", first.Problems));
        Dictionary<string, long> ids = SpriteIds(sheetPath);
        Assert.That(ids.Count, Is.EqualTo(64));
        AsepriteCharacterRecipe recipe = Recipe(Character, new AsepriteClip(file, CharacterGait.Run, false, naturalSpeed, run.Pivot));

        Import(RunFile(sheet, extraFrame: true), "Run");
        AssertKeepsIds(AsepriteCharacterWriter.Generate(recipe), ids, sheetPath, 9, "frame added");

        Import(RunFile(sheet, reversed: true), "Run");
        AssertKeepsIds(AsepriteCharacterWriter.Generate(recipe), ids, sheetPath, 8, "directions moved");
        Assert.That(File.ReadAllBytes(sheetPath), Is.EqualTo(File.ReadAllBytes(CharacterSheetWriter.SheetPath(rogue, run))));

        Import(RunFile(sheet, marked: true), "Run");
        AssertKeepsIds(AsepriteCharacterWriter.Generate(recipe), ids, sheetPath, 8, "layer added");
        Assert.That(File.ReadAllBytes(sheetPath), Is.Not.EqualTo(File.ReadAllBytes(CharacterSheetWriter.SheetPath(rogue, run))));

        Import(RunFile(sheet, pad: 8), "Run");
        AsepriteCharacterRecipe grown = Recipe(Character, new AsepriteClip(file, CharacterGait.Run, false, naturalSpeed, run.Pivot + new Vector2Int(8, 8)));
        AssertKeepsIds(AsepriteCharacterWriter.Generate(grown), ids, sheetPath, 8, "canvas resized");
        Sprite grownSprite = AssetDatabase.LoadAllAssetsAtPath(sheetPath).OfType<Sprite>().First();
        Assert.That(grownSprite.rect.size, Is.EqualTo(new Vector2(80f, 80f)));
        Assert.That(grownSprite.pivot, Is.EqualTo(new Vector2(40f, 20f)));
    }

    /// <summary>
    /// Checks a generation after an edit: no problem, the sheet's slices are every direction's
    /// <paramref name="frames"/> frames, each slice drawn before keeps its sprite ID, and the set's run points at the
    /// sheet's slices by direction and frame.
    /// </summary>
    private static void AssertKeepsIds(AsepriteCharacterReport report, Dictionary<string, long> ids, string sheetPath, int frames, string edit)
    {
        Assert.That(report.Problems, Is.Empty, edit);
        Dictionary<string, long> now = SpriteIds(sheetPath);
        Assert.That(now.Count, Is.EqualTo(CharacterDirections.Count * frames), edit);
        foreach (KeyValuePair<string, long> slice in ids.Where(s => now.ContainsKey(s.Key)))
        {
            Assert.That(now[slice.Key], Is.EqualTo(slice.Value), $"{edit}: {slice.Key}");
        }

        Assert.That(ids.Keys.Count(now.ContainsKey), Is.EqualTo(CharacterDirections.Count * Mathf.Min(frames, 8)), edit);
        CharacterAnimationClip clip = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(CharacterSheetWriter.SetPath(Character)).Find("Run");
        Assert.That(clip.FrameCount, Is.EqualTo(frames), edit);
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                string name = CharacterSheetWriter.SliceName(Character, "Run", direction, frame);
                Sprite sprite = clip.SpriteAt(direction, frame);
                Assert.That(sprite.name, Is.EqualTo(name), edit);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string _, out long fileId);
                Assert.That(fileId, Is.EqualTo(now[name]), $"{edit}: {name}");
            }
        }
    }

    /// <summary>
    /// Each sprite of the sheet at <paramref name="path"/> by name, with its local file ID.
    /// </summary>
    private static Dictionary<string, long> SpriteIds(string path)
    {
        Dictionary<string, long> ids = new();
        foreach (Sprite sprite in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>())
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string _, out long fileId);
            ids[sprite.name] = fileId;
        }

        return ids;
    }

    /// <summary>
    /// The project's baked sheet of <paramref name="clip"/>, read from its PNG, so no clip is baked again.
    /// </summary>
    private static CharacterSheet BakedSheet(CharacterRecipe recipe, CharacterClip clip)
    {
        Texture2D texture = new(2, 2, TextureFormat.RGBA32, false);
        try
        {
            texture.LoadImage(File.ReadAllBytes(CharacterSheetWriter.SheetPath(recipe, clip)));
            return new CharacterSheet(clip, clip.Motion.FrameCount, texture.GetPixels32(), Array.Empty<string>());
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }

    /// <summary>
    /// The baked run as a file, with a frame repeated at the end of every direction, the directions written from SE
    /// back to E, a visible layer drawing a mark in every frame, or the canvas grown by <paramref name="pad"/> on every
    /// side around the drawing.
    /// </summary>
    private static AsepriteFileWriter RunFile(CharacterSheet sheet, bool extraFrame = false, bool reversed = false, bool marked = false, int pad = 0)
    {
        CharacterClip clip = sheet.Clip;
        Vector2Int size = clip.FrameSize;
        int width = size.x + 2 * pad;
        int height = size.y + 2 * pad;
        AsepriteFileWriter writer = new(width, height);
        int body = writer.AddLayer("Body");
        int mark = marked ? writer.AddLayer("Mark") : -1;
        Color32[] canvas = new Color32[width * height];
        for (int i = 0; i < CharacterDirections.Count; i++)
        {
            int direction = reversed ? CharacterDirections.Count - 1 - i : i;
            int first = writer.FrameCount;
            int frames = sheet.FrameCount + (extraFrame ? 1 : 0);
            for (int f = 0; f < frames; f++)
            {
                int column = Mathf.Min(f, sheet.FrameCount - 1);
                int frame = writer.AddFrame(Mathf.RoundToInt(clip.Motion.FrameSeconds(column) * 1000f));
                Array.Clear(canvas, 0, canvas.Length);
                RectInt rect = CharacterSheet.FrameRect(direction, column, size);
                for (int y = 0; y < size.y; y++)
                {
                    Array.Copy(sheet.Pixels, (rect.y + size.y - 1 - y) * sheet.Width + rect.x, canvas, (y + pad) * width + pad, size.x);
                }

                writer.SetImage(body, frame, canvas);
                if (mark >= 0)
                {
                    writer.SetCel(mark, frame, new RectInt(pad + 1, pad + 1, 2, 2), Enumerable.Repeat(new Color32(255, 0, 255, 255), 4).ToArray());
                }
            }

            writer.AddTag(CharacterDirections.Name(direction), first, writer.FrameCount - 1);
        }

        return writer;
    }

    /// <summary>
    /// A clip of 8 x 8 px frames, two in each direction by default, each a 3 x 6 px block of its own colour at (2, 1)
    /// from the top left: <paramref name="framesIn"/> a direction, <paramref name="millisecondsOf"/> each (100 by
    /// default), <paramref name="marked"/> frames with the contact mark, <paramref name="empty"/> frames without a
    /// cel, each direction's tag named <paramref name="tagOf"/> (none for null) playing <paramref name="loopOf"/>.
    /// </summary>
    private static AsepriteFileWriter Clip(Func<int, int> framesIn = null, Func<int, int, int> millisecondsOf = null, Func<int, int, bool> marked = null, Func<int, string> tagOf = null, Func<int, int, bool> empty = null, Func<int, int> loopOf = null)
    {
        AsepriteFileWriter writer = new(8, 8);
        int body = writer.AddLayer("Body");
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            int first = writer.FrameCount;
            int frames = framesIn?.Invoke(direction) ?? 2;
            for (int f = 0; f < frames; f++)
            {
                int frame = writer.AddFrame(millisecondsOf?.Invoke(direction, f) ?? 100);
                if (empty?.Invoke(direction, f) != true)
                {
                    string mark = marked?.Invoke(direction, f) == true ? AsepriteFileWriter.ContactMarker : null;
                    writer.SetCel(body, frame, new RectInt(2, 1, 3, 6), Enumerable.Repeat(Colour(direction, f), 18).ToArray(), mark);
                }
            }

            string tag = tagOf != null ? tagOf(direction) : CharacterDirections.Name(direction);
            if (tag != null)
            {
                writer.AddTag(tag, first, writer.FrameCount - 1, loopOf?.Invoke(direction) ?? AsepriteFileWriter.LoopForward);
            }
        }

        return writer;
    }

    /// <summary>
    /// The colour of frame <paramref name="frame"/> of <paramref name="direction"/> in <see cref="Clip"/>'s files.
    /// </summary>
    private static Color32 Colour(int direction, int frame)
    {
        return new Color32((byte)(20 + direction * 25), (byte)(40 + frame * 50), 90, 255);
    }

    /// <summary>
    /// The pixel of a read clip's sheet at (<paramref name="x"/>, <paramref name="y"/>) from the top left of frame
    /// <paramref name="frame"/> of <paramref name="direction"/>.
    /// </summary>
    private static Color32 SheetPixel(AsepriteClipFrames frames, int direction, int frame, int x, int y)
    {
        RectInt rect = CharacterSheet.FrameRect(direction, frame, frames.FrameSize);
        return frames.Pixels[(rect.y + frames.FrameSize.y - 1 - y) * frames.SheetWidth + rect.x + x];
    }

    /// <summary>
    /// An unsaved recipe for <paramref name="name"/> of <paramref name="clips"/>, drawn with no prefab.
    /// </summary>
    private AsepriteCharacterRecipe Recipe(string name, params AsepriteClip[] clips)
    {
        return Recipe(null, name, clips);
    }

    /// <summary>
    /// An unsaved recipe for <paramref name="name"/> of <paramref name="clips"/>, drawn with <paramref name="prefab"/>.
    /// </summary>
    private AsepriteCharacterRecipe Recipe(GameObject prefab, string name, params AsepriteClip[] clips)
    {
        AsepriteCharacterRecipe recipe = AsepriteCharacterRecipe.Create(name, prefab, clips);
        created.Add(recipe);
        return recipe;
    }

    /// <summary>
    /// A path for <see cref="FileName"/> in a folder of its own under the project's Temp folder, deleted after the
    /// test; nothing there is imported.
    /// </summary>
    private string TempPath()
    {
        string folder = FileUtil.GetUniqueTempPathInProject();
        Directory.CreateDirectory(folder);
        tempFolders.Add(folder);
        return Path.Combine(folder, FileName);
    }

    /// <summary>
    /// Writes <paramref name="writer"/>'s file to a <see cref="TempPath"/> and returns the path.
    /// </summary>
    private string WriteTemp(AsepriteFileWriter writer)
    {
        string path = TempPath();
        writer.Write(path);
        return path;
    }

    /// <summary>
    /// Creates the test character's art folder, remembering the first folder on its way that did not exist so the
    /// tear down deletes it.
    /// </summary>
    private void EnsureArtFolder()
    {
        if (createdArtFolder != null)
        {
            return;
        }

        createdArtFolder = ArtFolder;
        for (string folder = ArtFolder; !AssetDatabase.IsValidFolder(folder); folder = Path.GetDirectoryName(folder).Replace('\\', '/'))
        {
            createdArtFolder = folder;
        }

        TerrainMaterialWriter.EnsureFolder(ArtFolder);
    }

    /// <summary>
    /// Writes <paramref name="writer"/>'s file as the test character's clip <paramref name="clip"/> under
    /// Assets/Art/Characters, imports it and returns its main asset.
    /// </summary>
    private Object Import(AsepriteFileWriter writer, string clip)
    {
        return ImportAs(writer, $"{Character}_{clip}");
    }

    /// <summary>
    /// Writes <paramref name="writer"/>'s file as <paramref name="name"/> in the test character's art folder under
    /// Assets/Art/Characters, imports it and returns its main asset.
    /// </summary>
    private Object ImportAs(AsepriteFileWriter writer, string name)
    {
        EnsureArtFolder();
        string path = $"{ArtFolder}/{name}.aseprite";
        writer.Write(path);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadMainAssetAtPath(path);
    }

    /// <summary>
    /// Writes <paramref name="writer"/>'s file as <paramref name="name"/> in a folder outside Assets/Art/Characters,
    /// imports it and returns its main asset.
    /// </summary>
    private static Object ImportOutside(AsepriteFileWriter writer, string name)
    {
        TerrainMaterialWriter.EnsureFolder(OutsideFolder);
        string path = $"{OutsideFolder}/{name}.aseprite";
        writer.Write(path);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadMainAssetAtPath(path);
    }
}
