using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for <see cref="WorldHealthBar"/>: the fill tracking health, hiding while dead, the weasel prefab's bar, an
/// instance of the world bar component drawn with the UI skin (Docs/DesignSystem.md, Phase 5), and the player prefab's,
/// an instance of the world bar's player variant with its cast bar (Docs/UiRevamp.md, Phase 4).
/// </summary>
public class WorldHealthBarTests
{
    private const float ArtPixelsPerUnit = 32f;
    private const string PartsFolder = "Assets/Sprites/Characters/Adventurer/Parts";
    private const string AdventurerSet = "Assets/Sprites/Characters/Adventurer/Adventurer_AnimationSet.asset";

    private readonly List<Object> createdObjects = new();
    private Health health;
    private WorldHealthBar bar;
    private SpriteRenderer fill;

    /// <summary>
    /// Builds a unit with a <see cref="Health"/> and a child health bar whose sliced frame and fill use the skin's bar
    /// sprites, 22 x 4 and 20 x 2 art px.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        GameObject unit = Track(new GameObject("Unit"));
        health = unit.AddComponent<Health>();
        GameObject barObject = new("HealthBar");
        barObject.transform.SetParent(unit.transform);
        SpriteRenderer frame = Part(barObject, "Frame", Skin(UiSkinPiece.BarFrame), new Vector2(22f, 4f) / ArtPixelsPerUnit);
        fill = Part(barObject, "Fill", Skin(UiSkinPiece.BarFill), new Vector2(20f, 2f) / ArtPixelsPerUnit);
        bar = barObject.AddComponent<WorldHealthBar>();
        bar.Configure(health, frame, fill);
    }

    /// <summary>
    /// Destroys the unit, and with it the health bar.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object created in createdObjects)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        createdObjects.Clear();
    }

    /// <summary>
    /// Damage shrinks the fill to the health fraction while its left edge stays put, so the bar empties from the
    /// right.
    /// </summary>
    [Test]
    public void Damage_ShrinksTheFillTowardTheLeftEdge()
    {
        bar.Refresh();
        float fullWidth = fill.bounds.size.x;
        float leftEdge = fill.bounds.min.x;
        Assert.That(bar.FillFraction, Is.EqualTo(1f));

        health.ApplyDamage(3);
        bar.Refresh();
        float expected = (float)(health.MaxHealth - 3) / health.MaxHealth;

        Assert.That(bar.FillFraction, Is.EqualTo(expected).Within(0.001f));
        Assert.That(fill.bounds.size.x, Is.EqualTo(fullWidth * expected).Within(0.001f));
        Assert.That(fill.bounds.min.x, Is.EqualTo(leftEdge).Within(0.001f), "The bar empties from the right, like League minion bars.");
    }

    /// <summary>
    /// A health value set by a network sync, which raises no events, still shows on the next refresh because the
    /// bar polls health.
    /// </summary>
    [Test]
    public void SilentHealthSyncs_AreShownToo()
    {
        bar.Refresh();

        health.SyncTo(4);
        bar.Refresh();

        Assert.That(bar.FillFraction, Is.EqualTo(4f / health.MaxHealth).Within(0.001f), "Network syncs set health without events, so the bar polls it.");
    }

    /// <summary>
    /// The bar hides, fill included, while the unit is dead and shows full again once health is restored.
    /// </summary>
    [Test]
    public void TheBarHidesWhileDead_AndReturnsOnRestore()
    {
        health.ApplyDamage(health.MaxHealth);
        bar.Refresh();
        Assert.That(bar.IsShown, Is.False);
        Assert.That(fill.enabled, Is.False);

        health.Restore();
        bar.Refresh();
        Assert.That(bar.IsShown, Is.True);
        Assert.That(bar.FillFraction, Is.EqualTo(1f));
    }

    /// <summary>
    /// The weasel prefab's health bar is an instance of the world bar component and reads the weasel's health: the
    /// skin's bar frame, 22 x 4 art px, in HealthTrack, with its bar fill, 2 px tall and 1 px inside it, in Health,
    /// both sliced so their corners keep at any width, with unlit materials on the Overlay sorting layer, which
    /// sorts above units; four status pips above it, unscaled, so 5 art px (Docs/UiRevamp.md, Phase 4); the whole bar
    /// on whole art px above the weasel's head.
    /// </summary>
    [Test]
    public void WeaselPrefab_HasTheSkinnedWorldBarOnTheOverlayLayerAboveItsHead()
    {
        GameObject weasel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mobs/Weasel.prefab");
        WorldHealthBar prefabBar = weasel.GetComponentInChildren<WorldHealthBar>(true);
        Assert.That(prefabBar, Is.Not.Null);
        Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prefabBar.gameObject), Is.EqualTo("Assets/Prefabs/UI/Components/WorldBar.prefab"));

        SerializedObject serialized = new(prefabBar);
        Assert.That(serialized.FindProperty("health").objectReferenceValue, Is.SameAs(weasel.GetComponent<Health>()));
        SpriteRenderer prefabFrame = (SpriteRenderer)serialized.FindProperty("frame").objectReferenceValue;
        SpriteRenderer prefabFill = (SpriteRenderer)serialized.FindProperty("fill").objectReferenceValue;
        Assert.That(prefabFrame.sprite, Is.EqualTo(Skin(UiSkinPiece.BarFrame)));
        Assert.That(prefabFill.sprite, Is.EqualTo(Skin(UiSkinPiece.BarFill)));
        Assert.That(prefabFrame.drawMode, Is.EqualTo(SpriteDrawMode.Sliced));
        Assert.That(prefabFill.drawMode, Is.EqualTo(SpriteDrawMode.Sliced), "Sliced keeps the corners at any width.");
        Assert.That(prefabFrame.size * ArtPixelsPerUnit, Is.EqualTo(new Vector2(22f, 4f)));
        Assert.That(prefabFill.size * ArtPixelsPerUnit, Is.EqualTo(new Vector2(20f, 2f)));
        Assert.That(prefabFrame.GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.HealthTrack));
        Assert.That(prefabFill.GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.Health));
        Assert.That(prefabFrame.sortingLayerName, Is.EqualTo("Overlay"));
        Assert.That(prefabFill.sortingLayerName, Is.EqualTo("Overlay"));
        Assert.That(prefabFill.sortingOrder, Is.GreaterThan(prefabFrame.sortingOrder));
        Assert.That(prefabFill.sharedMaterial.shader.name, Does.Contain("Unlit"), "Overlay is not lit by the areas' 2D lights; a lit material would draw black.");
        Assert.That(prefabFrame.sharedMaterial.shader.name, Does.Contain("Unlit"));
        Vector3 position = prefabBar.transform.localPosition * ArtPixelsPerUnit;
        Assert.That(position.y, Is.GreaterThan(ArtPixelsPerUnit), "The bar sits above the weasel's head.");
        Assert.That(new Vector2(position.x, position.y), Is.EqualTo(new Vector2(Mathf.Round(position.x), Mathf.Round(position.y))), "The bar sits on whole art px.");
        Assert.That(SortingLayer.GetLayerValueFromName("Overlay"), Is.GreaterThan(SortingLayer.GetLayerValueFromName("Entities")), "Bars draw over every unit.");
        int pips = 0;
        foreach (SpriteRenderer icon in prefabBar.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (icon.name.StartsWith("StatusIcon"))
            {
                pips++;
                Assert.That(icon.sprite, Is.EqualTo(Skin(UiSkinPiece.Pip)), $"{icon.name} is a pip.");
                Assert.That(icon.transform.localScale, Is.EqualTo(Vector3.one), $"{icon.name} draws the pip's 5 px on the art grid.");
                Assert.That(icon.transform.localPosition.y, Is.GreaterThan(prefabFrame.transform.localPosition.y + prefabFrame.size.y * 0.5f), $"{icon.name} sits above the bar.");
            }
        }

        Assert.That(pips, Is.EqualTo(4));
    }

    /// <summary>
    /// The player prefab's health bar is an instance of the world bar's player variant (Docs/UiRevamp.md, Phase 4),
    /// reading the player's health, statuses and abilities: the world bar with its fill in HealthPlayer, and under it a
    /// cast bar, the skin's bar frame 22 x 3 art px in HealthTrack sharing the bar's bottom border, with its bar fill
    /// 20 x 1 inside it in Accent, read against the cast frame, both hidden until a cast, unlit on the Overlay layer
    /// above the bar's frame; the bar is on whole art px, and the cast bar's bottom edge clears the top of every look the
    /// adventurer's parts can make while it stands, idles, walks or runs, outline included.
    /// </summary>
    [Test]
    public void PlayerPrefab_HasThePlayerWorldBar_WithACastBarAboveItsHead()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/Player.prefab");
        WorldHealthBar prefabBar = player.GetComponentInChildren<WorldHealthBar>(true);
        Assert.That(prefabBar, Is.Not.Null);
        Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prefabBar.gameObject), Is.EqualTo("Assets/Prefabs/UI/Components/WorldBarPlayer.prefab"));
        GameObject variant = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Components/WorldBarPlayer.prefab");
        Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(variant)), Is.EqualTo("Assets/Prefabs/UI/Components/WorldBar.prefab"), "A variant of the world bar.");

        SerializedObject serialized = new(prefabBar);
        Assert.That(serialized.FindProperty("health").objectReferenceValue, Is.SameAs(player.GetComponent<Health>()));
        SpriteRenderer barFrame = (SpriteRenderer)serialized.FindProperty("frame").objectReferenceValue;
        SpriteRenderer barFill = (SpriteRenderer)serialized.FindProperty("fill").objectReferenceValue;
        Assert.That(barFill.GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.HealthPlayer), "Players' bars are green, mobs' red.");
        Assert.That(new SerializedObject(prefabBar.GetComponent<WorldStatusIcons>()).FindProperty("statuses").objectReferenceValue, Is.SameAs(player.GetComponent<StatusEffects>()));

        WorldCastBar castBar = prefabBar.GetComponent<WorldCastBar>();
        Assert.That(castBar, Is.Not.Null);
        SerializedObject cast = new(castBar);
        Assert.That(cast.FindProperty("abilities").objectReferenceValue, Is.SameAs(player.GetComponent<PlayerAbilities>()));
        Assert.That(cast.FindProperty("health").objectReferenceValue, Is.SameAs(player.GetComponent<Health>()));
        SpriteRenderer castFrame = (SpriteRenderer)cast.FindProperty("frame").objectReferenceValue;
        SpriteRenderer castFill = (SpriteRenderer)cast.FindProperty("fill").objectReferenceValue;
        Assert.That(castFrame.sprite, Is.EqualTo(Skin(UiSkinPiece.BarFrame)));
        Assert.That(castFill.sprite, Is.EqualTo(Skin(UiSkinPiece.BarFill)));
        Assert.That(castFrame.drawMode, Is.EqualTo(SpriteDrawMode.Sliced));
        Assert.That(castFill.drawMode, Is.EqualTo(SpriteDrawMode.Sliced));
        Assert.That(castFrame.size * ArtPixelsPerUnit, Is.EqualTo(new Vector2(22f, 3f)));
        Assert.That(castFill.size * ArtPixelsPerUnit, Is.EqualTo(new Vector2(20f, 1f)));
        Assert.That(castFrame.GetComponent<ThemedGraphic>().Role, Is.EqualTo(UiColour.HealthTrack));
        ThemedGraphic castFillTheme = castFill.GetComponent<ThemedGraphic>();
        Assert.That(castFillTheme.Role, Is.EqualTo(UiColour.Accent));
        Assert.That(castFillTheme.Track, Is.SameAs(castFrame.GetComponent<ThemedGraphic>()), "The cast fill is measured against its frame.");
        Assert.That(castFrame.enabled || castFill.enabled, Is.False, "Hidden until a cast.");
        foreach (SpriteRenderer part in new[] { castFrame, castFill })
        {
            Assert.That(part.sortingLayerName, Is.EqualTo("Overlay"));
            Assert.That(part.sharedMaterial.shader.name, Does.Contain("Unlit"));
        }

        Assert.That(castFill.sortingOrder, Is.GreaterThan(castFrame.sortingOrder));
        float barBottom = (barFrame.transform.localPosition.y - barFrame.size.y * 0.5f) * ArtPixelsPerUnit;
        float castTop = (castFrame.transform.localPosition.y + castFrame.size.y * 0.5f) * ArtPixelsPerUnit;
        float castBottom = (castFrame.transform.localPosition.y - castFrame.size.y * 0.5f) * ArtPixelsPerUnit;
        Assert.That(castTop, Is.EqualTo(barBottom + 1f).Within(1e-4f), "The cast bar shares the bar's bottom border.");
        Assert.That(castFill.transform.localPosition.y, Is.EqualTo(castFrame.transform.localPosition.y).Within(1e-6f), "The cast fill sits inside its frame.");

        Vector3 position = prefabBar.transform.localPosition * ArtPixelsPerUnit;
        Assert.That(new Vector2(position.x, position.y), Is.EqualTo(new Vector2(Mathf.Round(position.x), Mathf.Round(position.y))), "The bar sits on whole art px.");
        Assert.That(position.y + castBottom, Is.GreaterThanOrEqualTo(TallestStandingLook()), "The cast bar clears the tallest look.");
    }

    /// <summary>
    /// How many art px above the feet the adventurer's tallest look reaches while it stands, idles, walks or runs: the
    /// highest top of any part's layer in those clips, plus the 1 px outline drawn around the character.
    /// </summary>
    private static int TallestStandingLook()
    {
        CharacterAnimationSet set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(AdventurerSet);
        int tallest = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:TextAsset", new[] { PartsFolder }))
        {
            CharacterLayer layer = CharacterLayer.Read(AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(guid)).bytes);
            for (int c = 0; c < layer.Clips.Length && c < set.Clips.Count; c++)
            {
                string clip = set.Clips[c].Name;
                if (!clip.StartsWith("Idle") && clip != "Walk" && clip != "Run" && clip != "Sprint")
                {
                    continue;
                }

                CharacterLayerClip layerClip = layer.Clips[c];
                foreach (CharacterLayerFrame frame in layerClip.Frames)
                {
                    if (frame != null && frame.Box.height > 0)
                    {
                        tallest = Mathf.Max(tallest, frame.Box.yMax - layerClip.Feet.y + 1);
                    }
                }
            }
        }

        Assert.That(tallest, Is.GreaterThan(30), "The parts' layers were read.");
        return tallest;
    }

    /// <summary>
    /// The skin sprite written for <paramref name="piece"/>.
    /// </summary>
    private static Sprite Skin(UiSkinPiece piece)
    {
        return AssetDatabase.LoadAssetAtPath<Sprite>(UiSkinRecipe.PathOf(piece));
    }

    /// <summary>
    /// Creates a child sprite renderer of <paramref name="parent"/> that draws <paramref name="sprite"/> sliced
    /// at <paramref name="size"/>.
    /// </summary>
    private SpriteRenderer Part(GameObject parent, string name, Sprite sprite, Vector2 size)
    {
        GameObject part = new(name);
        part.transform.SetParent(parent.transform);
        SpriteRenderer renderer = part.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.drawMode = SpriteDrawMode.Sliced;
        renderer.size = size;
        return renderer;
    }

    /// <summary>
    /// Records <paramref name="created"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T created) where T : Object
    {
        createdObjects.Add(created);
        return created;
    }
}
