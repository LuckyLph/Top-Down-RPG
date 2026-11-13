using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for <see cref="CharacterPreviewStage"/> (Docs/UiRevamp.md, Phase 7) on the Character screen prefab's stage, on a
/// manual clock: it draws the sheets' own pixels exactly into its texture, its feet on their row, wherever it sits and
/// however its parents scale it; a pick and a previewed swatch recolour it; it turns and poses, the attacking pose
/// playing the shown weapon's attacks in turn with a pause between.
/// </summary>
public class CharacterPreviewStageTests
{
    private const string ScreenPath = "Assets/Prefabs/UI/CharacterScreen.prefab";

    private GameObject parent;
    private CharacterPreviewStage stage;
    private UserSettings settings;
    private CharacterOutfits outfits;
    private ManualClock clock;
    private PlayerWeapon sword;

    /// <summary>
    /// Instantiates the screen under a parent scaled and moved as a canvas would, and readies its stage on in-memory
    /// settings and a manual clock.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        parent = new GameObject("Canvas", typeof(RectTransform));
        parent.transform.position = new Vector3(960f, 540f, 0f);
        parent.transform.localScale = new Vector3(3f, 3f, 1f);
        GameObject screen = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ScreenPath), parent.transform);
        stage = screen.GetComponent<CharacterScreenView>().Stage;
        settings = new UserSettings(new MemorySaveStore());
        outfits = new CharacterOutfits(0, 500);
        clock = new ManualClock();
        sword = AssetDatabase.LoadAssetAtPath<PlayerWeapon>("Assets/Data/Weapons/Sword.asset");
        stage.Look.Construct(settings, outfits);
        stage.Configure(stage.Visuals, stage.Look, sword, clock);
    }

    /// <summary>
    /// Hides the stage and destroys the screen and the outfits.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        stage.Hide();
        outfits.Dispose();
        Object.DestroyImmediate(parent);
    }

    /// <summary>
    /// Shown, the stage stands facing south and draws the idle's south frame into its texture exactly as the sheet holds
    /// it, its feet 24 texels up the middle of the 80 texel texture, around it nothing: no light, sun or night reaches
    /// it, and its parents' scale is undone.
    /// </summary>
    [Test]
    public void TheStage_DrawsTheSheetsOwnPixels()
    {
        stage.Show(null);
        Assert.That(stage.Direction, Is.EqualTo(CharacterPreviewStage.South));
        Assert.That(stage.Pose, Is.EqualTo(CharacterPreviewPose.Standing));
        Color32[] drawn = Frame();
        Sprite sprite = stage.Visuals.AnimationSet.Locomotion(CharacterGait.Idle).SpriteAt(CharacterPreviewStage.South, 0);
        Color32[] sheet = SheetAt(sprite);
        int opaque = 0;
        int differ = 0;
        for (int i = 0; i < drawn.Length; i++)
        {
            bool shown = sheet[i].a > 0;
            opaque += shown ? 1 : 0;
            differ += shown ? (drawn[i].Equals(sheet[i]) ? 0 : 1) : (drawn[i].a == 0 ? 0 : 1);
        }

        Assert.That(opaque, Is.GreaterThan(400));
        Assert.That(differ, Is.Zero, "Every texel is the sheet's, the clear ones clear.");
    }

    /// <summary>
    /// A colour the settings pick recolours the drawing, a previewed swatch too until the picked colours show again.
    /// </summary>
    [Test]
    public void APickOrAPreview_RecoloursTheDrawing()
    {
        stage.Show(null);
        Color32[] own = Frame();
        settings.SetCharacterColour("Tunic", "Red");
        Color32[] red = Frame();
        Assert.That(red, Is.Not.EqualTo(own), "The settings' red tunic draws.");

        settings.SetCharacterColour("Tunic", string.Empty);
        CharacterColours colours = stage.Look.Colours;
        stage.PreviewColour(colours.IndexOf("Tunic"), "Red");
        Assert.That(Frame(), Is.EqualTo(red), "The previewed red draws as the picked one did.");
        stage.ShowPickedColours();
        Assert.That(Frame(), Is.EqualTo(own));
    }

    /// <summary>
    /// The turns go through the 8 directions either way and wrap; the running pose runs at a running speed; the
    /// attacking pose plays the shown weapon's attacks in turn, standing between them for the pause, and the next pose
    /// stops the attack showing.
    /// </summary>
    [Test]
    public void TheStage_TurnsAndPoses()
    {
        stage.Show(null);
        stage.Turn(-1);
        Assert.That(stage.Direction, Is.EqualTo(5));
        stage.Turn(4);
        Assert.That(stage.Direction, Is.EqualTo(1));
        stage.Turn(-10);
        Assert.That(stage.Direction, Is.EqualTo(7));

        stage.NextPose();
        Step();
        Assert.That(stage.Pose, Is.EqualTo(CharacterPreviewPose.Running));
        Assert.That(stage.Visuals.Animator.Gait, Is.EqualTo(CharacterGait.Run));

        stage.NextPose();
        Step();
        CharacterAnimator animator = stage.Visuals.Animator;
        CharacterAnimationSet set = stage.Visuals.AnimationSet;
        Assert.That(animator.IsActing, Is.True);
        Assert.That(animator.Clip, Is.EqualTo(set.Find(sword.AttackClipOf(0))));
        float duration = sword.Timeline.Duration;
        Run(duration + 0.05f);
        Assert.That(animator.IsActing, Is.False, "It stands for the pause after an attack.");
        Run(0.4f);
        Assert.That(animator.IsActing, Is.False, "Still within the pause.");
        Run(0.1f);
        Assert.That(animator.IsActing, Is.True);
        Assert.That(animator.Clip, Is.EqualTo(set.Find(sword.AttackClipOf(1))), "Then the sword's other attack.");

        stage.NextPose();
        Step();
        Assert.That(stage.Pose, Is.EqualTo(CharacterPreviewPose.Standing));
        Assert.That(animator.IsActing, Is.False);
    }

    /// <summary>
    /// Runs the stage's frame: its movement, the visuals' animation, then its drawing.
    /// </summary>
    private void Step()
    {
        stage.Move();
        stage.Visuals.Tick();
        stage.Draw();
    }

    /// <summary>
    /// Runs frames of a thirtieth of a second for <paramref name="seconds"/>.
    /// </summary>
    private void Run(float seconds)
    {
        for (float run = 0f; run < seconds - 0.0001f; run += 1f / 30f)
        {
            clock.Advance(Mathf.Min(1f / 30f, seconds - run));
            Step();
        }
    }

    /// <summary>
    /// Runs a frame and reads the stage's texture back.
    /// </summary>
    private Color32[] Frame()
    {
        Step();
        RenderTexture texture = (RenderTexture)stage.Texture;
        Texture2D pixels = new(texture.width, texture.height, TextureFormat.RGBA32, false, false);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = texture;
        pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        pixels.Apply();
        RenderTexture.active = previous;
        Color32[] read = pixels.GetPixels32();
        Object.DestroyImmediate(pixels);
        return read;
    }

    /// <summary>
    /// <paramref name="sprite"/>'s texels read from its sheet's file, laid out as the stage draws them: its pivot on the
    /// feet's row in the middle of the texture.
    /// </summary>
    private static Color32[] SheetAt(Sprite sprite)
    {
        Texture2D file = new(2, 2);
        file.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sprite.texture)));
        Color32[] all = file.GetPixels32();
        int size = CharacterPreviewStage.Size;
        Color32[] laid = new Color32[size * size];
        int originX = size / 2 - Mathf.RoundToInt(sprite.pivot.x);
        int originY = CharacterPreviewStage.FeetRow - Mathf.RoundToInt(sprite.pivot.y);
        for (int y = 0; y < (int)sprite.rect.height; y++)
        {
            for (int x = 0; x < (int)sprite.rect.width; x++)
            {
                int tx = originX + x;
                int ty = originY + y;
                if (tx >= 0 && ty >= 0 && tx < size && ty < size)
                {
                    laid[ty * size + tx] = all[((int)sprite.rect.y + y) * file.width + (int)sprite.rect.x + x];
                }
            }
        }

        Object.DestroyImmediate(file);
        return laid;
    }
}
