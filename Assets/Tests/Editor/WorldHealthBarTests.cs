using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class WorldHealthBarTests
{
    private readonly List<Object> createdObjects = new();
    private Health health;
    private WorldHealthBar bar;
    private SpriteRenderer fill;

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

    [Test]
    public void SilentHealthSyncs_AreShownToo()
    {
        bar.Refresh();

        health.SyncTo(4);
        bar.Refresh();

        Assert.That(bar.FillFraction, Is.EqualTo(4f / health.MaxHealth).Within(0.001f), "Network syncs set health without events, so the bar polls it.");
    }

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

    private T Track<T>(T created) where T : Object
    {
        createdObjects.Add(created);
        return created;
    }
}
