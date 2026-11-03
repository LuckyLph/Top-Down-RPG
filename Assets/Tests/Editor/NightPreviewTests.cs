using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The night previewed in the editor (Docs/LightingGaps.md, Phase 6): at night a light prop's light burns at its
/// strength times the night lights' share, the sun casts nothing and water reflects, while a sleeping caster sleeps on;
/// at noon the light stays off and the sun casts; hidden, or put back before a scene saves, every light is as it was and
/// the shader globals as the editor has them, and the preview shows again after.
/// </summary>
public class NightPreviewTests
{
    private const float Night = 0.88f;
    private const float Noon = 0.4f;

    private GameObject lamp;
    private GameObject wall;

    /// <summary>
    /// A light prop, its light off at strength 0.3, and a sleeping caster.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        NightPreview.instance.Hide();
        lamp = new GameObject("Lamp");
        Light2D light = lamp.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.intensity = 0.3f;
        light.enabled = false;
        lamp.AddComponent<PropLight>().Configure(light, 1.2f, 0f);
        wall = new GameObject("Wall");
        wall.AddComponent<ShadowCaster2D>().enabled = false;
    }

    /// <summary>
    /// Hides the preview and destroys what the test made.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        NightPreview.instance.Hide();
        Object.DestroyImmediate(lamp);
        Object.DestroyImmediate(wall);
    }

    /// <summary>
    /// Shown at night the light burns and the caster wakes; at noon the light is off; hidden, both are as they were.
    /// </summary>
    [Test]
    public void ShownAtNight_TheLightsBurn_AndHiddenEverythingIsAsItWas()
    {
        DayNightSettings settings = AssetDatabase.LoadAssetAtPath<DayNightSettings>(NightPreview.SettingsPath);
        Light2D light = lamp.GetComponent<Light2D>();
        ShadowCaster2D caster = wall.GetComponent<ShadowCaster2D>();
        float share = settings.LightAt(Night).NightLights;
        Assert.That(share, Is.GreaterThan(0f));

        NightPreview.instance.Show(Night);
        Assert.That(light.enabled, Is.True);
        Assert.That(light.intensity, Is.EqualTo(1.2f * share).Within(1e-5f));
        Assert.That(caster.enabled, Is.False, "A sleeping caster sleeps on: woken in the editor it would write its mesh's state.");
        Assert.That(Shader.GetGlobalVector(SunShadowGlobals.SunShadowId).z, Is.Zero, "No sun at night.");
        Assert.That(Shader.GetGlobalVector(DayNightPresenter.ReflectionId).w, Is.GreaterThan(0f), "Water reflects.");

        NightPreview.instance.Show(Noon);
        Assert.That(light.enabled, Is.False, "No night lights at noon.");
        Assert.That(caster.enabled, Is.False);
        Assert.That(Shader.GetGlobalVector(SunShadowGlobals.SunShadowId).z, Is.EqualTo(1f), "The sun casts at noon.");

        NightPreview.instance.Hide();
        Assert.That(light.enabled, Is.False);
        Assert.That(light.intensity, Is.EqualTo(0.3f));
        Assert.That(caster.enabled, Is.False);
        Assert.That(Shader.GetGlobalVector(SunShadowGlobals.SunShadowId), Is.EqualTo(Vector4.zero));
        Assert.That(Shader.GetGlobalVector(DayNightPresenter.ReflectionId), Is.EqualTo(Vector4.zero));
        Assert.That(Shader.GetGlobalVector(DayNightPresenter.ReliefId), Is.EqualTo(Vector4.zero));
    }

    /// <summary>
    /// Put back for a save, the light is as authored; shown again after, it burns again.
    /// </summary>
    [Test]
    public void BeforeASave_ThePreviewIsPutBack_AndShownAgainAfter()
    {
        Light2D light = lamp.GetComponent<Light2D>();
        ShadowCaster2D caster = wall.GetComponent<ShadowCaster2D>();
        NightPreview.instance.Show(Night);

        NightPreview.instance.Restore();
        Assert.That(light.enabled, Is.False);
        Assert.That(light.intensity, Is.EqualTo(0.3f));
        Assert.That(caster.enabled, Is.False);

        NightPreview.instance.Apply();
        Assert.That(light.enabled, Is.True);
    }
}
