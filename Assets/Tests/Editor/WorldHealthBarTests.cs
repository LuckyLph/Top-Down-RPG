using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for <see cref="WorldHealthBar"/>: the fill tracking health, hiding while dead, and the weasel prefab's
/// bar, an instance of the world bar component drawn with the UI skin (Docs/DesignSystem.md, Phase 5).
/// </summary>
public class WorldHealthBarTests
{
    private const float ArtPixelsPerUnit = 32f;

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
    /// both sliced so their cut corners keep at any width, with unlit materials on the Overlay sorting layer, which
    /// sorts above units; four status pips above it; the whole bar on whole art px above the weasel's head.
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
        Assert.That(prefabFill.drawMode, Is.EqualTo(SpriteDrawMode.Sliced), "Sliced keeps the cut corners at any width.");
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
                Assert.That(icon.transform.localPosition.y, Is.GreaterThan(prefabFrame.transform.localPosition.y + prefabFrame.size.y * 0.5f), $"{icon.name} sits above the bar.");
            }
        }

        Assert.That(pips, Is.EqualTo(4));
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
