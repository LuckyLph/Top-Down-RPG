using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for <see cref="WorldHealthBar"/>: the fill tracking health, hiding while dead, and the weasel prefab's
/// bar setup.
/// </summary>
public class WorldHealthBarTests
{
    private readonly List<Object> createdObjects = new();
    private Health health;
    private WorldHealthBar bar;
    private SpriteRenderer fill;

    /// <summary>
    /// Builds a unit with a <see cref="Health"/> and a child health bar whose sliced frame and fill use the
    /// project's rounded bar sprite.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        GameObject unit = Track(new GameObject("Unit"));
        health = unit.AddComponent<Health>();
        GameObject barObject = new("HealthBar");
        barObject.transform.SetParent(unit.transform);
        Sprite rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/RoundedBar.png");
        SpriteRenderer frame = Part(barObject, "Frame", rounded, new Vector2(0.66f, 0.075f));
        fill = Part(barObject, "Fill", rounded, new Vector2(0.62f, 0.04f));
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
    /// The weasel prefab's health bar reads the weasel's health and draws a thin, sliced, bright red fill over
    /// its frame with unlit materials on the Overlay sorting layer, which sorts above units, and sits above the
    /// weasel's head.
    /// </summary>
    [Test]
    public void WeaselPrefab_HasAThinBarOnTheOverlayLayerAboveItsHead()
    {
        GameObject weasel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mobs/Weasel.prefab");
        WorldHealthBar prefabBar = weasel.GetComponentInChildren<WorldHealthBar>(true);
        Assert.That(prefabBar, Is.Not.Null);

        SerializedObject serialized = new(prefabBar);
        Assert.That(serialized.FindProperty("health").objectReferenceValue, Is.SameAs(weasel.GetComponent<Health>()));
        SpriteRenderer prefabFrame = (SpriteRenderer)serialized.FindProperty("frame").objectReferenceValue;
        SpriteRenderer prefabFill = (SpriteRenderer)serialized.FindProperty("fill").objectReferenceValue;
        Assert.That(prefabFrame.sortingLayerName, Is.EqualTo("Overlay"));
        Assert.That(prefabFill.sortingLayerName, Is.EqualTo("Overlay"));
        Assert.That(prefabFill.sortingOrder, Is.GreaterThan(prefabFrame.sortingOrder));
        Assert.That(prefabFill.drawMode, Is.EqualTo(SpriteDrawMode.Sliced), "Sliced keeps the rounded corners at any width.");
        Assert.That(prefabFill.sprite.border.x, Is.GreaterThan(0f));
        Assert.That(prefabFrame.size.y * prefabFrame.transform.localScale.y, Is.LessThanOrEqualTo(0.08f), "The bar is thin.");
        Assert.That(prefabFill.size.y, Is.LessThan(prefabFrame.size.y));
        Assert.That(prefabFill.color.r, Is.GreaterThan(0.95f), "The fill is a bright red.");
        Assert.That(prefabFill.sharedMaterial.shader.name, Does.Contain("Unlit"), "Overlay is not lit by the areas' 2D lights; a lit material would draw black.");
        Assert.That(prefabFrame.sharedMaterial.shader.name, Does.Contain("Unlit"));
        Assert.That(prefabBar.transform.localPosition.y, Is.GreaterThan(1f), "The bar sits above the weasel's head.");
        Assert.That(SortingLayer.GetLayerValueFromName("Overlay"), Is.GreaterThan(SortingLayer.GetLayerValueFromName("Entities")), "Bars draw over every unit.");
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
