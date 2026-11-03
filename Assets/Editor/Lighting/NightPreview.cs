using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// A time of day previewed in the editor (Docs/LightingGaps.md, Phase 6), from the Scene view's Time of Day overlay
/// (<see cref="TimeOfDayOverlay"/>): the open scenes lit as <see cref="DayNightPresenter"/> and
/// <see cref="PropLighting"/> light them in play at that phase of the shipped day (an outdoor world's global lights
/// taking the sky and the sun, the sun's shadows, the light props' lights and lava's glow burning at the night lights'
/// share without flicker, water's reflections and the relief under the lights), without the region skies, the clouds
/// or the props' lamp shadows: a prop's caster woken in the editor writes its mesh's state into its scene. What it
/// changes on the scenes' objects it records first and puts back when hidden, before any scene saves (showing it again
/// after) and before play starts, so nothing of it is ever saved; its state outlives script reloads.
/// </summary>
public sealed class NightPreview : ScriptableSingleton<NightPreview>
{
    public const string SettingsPath = "Assets/Data/Lighting/DayNight.asset";

    [SerializeField] private bool shown;
    [SerializeField] private float phase = 0.88f;
    [SerializeField] private bool resumeAfterPlay;
    [SerializeField] private List<LightRecord> lights = new();

    /// <summary>
    /// A Light 2D as it was before the preview changed it: every serialized field, so the light's mesh bounds, which it
    /// works out again while it burns, go back too.
    /// </summary>
    [Serializable]
    private struct LightRecord
    {
        public Light2D light;
        public bool enabled;
        public string state;
    }

    /// <summary>
    /// Whether the open scenes show the previewed time of day.
    /// </summary>
    public bool Shown => shown;

    /// <summary>
    /// The phase of the day previewed, 0 to 1 as <see cref="DayNightSettings.LightAt"/> takes it.
    /// </summary>
    public float Phase => phase;

    /// <summary>
    /// Lights the open scenes at <paramref name="atPhase"/> of the shipped day.
    /// </summary>
    public void Show(float atPhase)
    {
        phase = Mathf.Repeat(atPhase, 1f);
        shown = true;
        Apply();
    }

    /// <summary>
    /// Puts the open scenes back as they were and clears the lighting's shader globals, as the editor has them.
    /// </summary>
    public void Hide()
    {
        shown = false;
        Restore();
        SunShadowGlobals.Set(Color.black, Vector2.zero, false);
        SunShadowGlobals.SetClouds(Vector2.zero, 0f, 0f, 0f, 0f);
        Shader.SetGlobalVector(DayNightPresenter.ReflectionId, Vector4.zero);
        Shader.SetGlobalVector(DayNightPresenter.ReliefId, Vector4.zero);
        SceneView.RepaintAll();
    }

    /// <summary>
    /// Puts back what the preview changed and, while it is shown, lights the open scenes again at its phase: after a
    /// scene opens or saves, and as the overlay's controls change.
    /// </summary>
    internal void Apply()
    {
        Restore();
        if (!shown)
        {
            return;
        }

        DayNightSettings settings = AssetDatabase.LoadAssetAtPath<DayNightSettings>(SettingsPath);
        if (settings == null)
        {
            Debug.LogError($"{SettingsPath} is missing; the night preview shows nothing.");
            return;
        }

        DayLight day = settings.LightAt(phase);
        SunState sun = settings.SunAt(phase);
        bool outdoors = true;
        Color sky = day.Total;
        foreach (WorldLighting lighting in Object.FindObjectsByType<WorldLighting>(FindObjectsInactive.Include))
        {
            foreach (Light2D light in lighting.GetComponentsInChildren<Light2D>(true))
            {
                if (light.lightType == Light2D.LightType.Global)
                {
                    Record(light);
                }
            }

            if (lighting.Profile == WorldLightingProfile.Outdoor)
            {
                lighting.ApplySky(day.Total);
            }
            else
            {
                outdoors = false;
                sky = lighting.GlobalLight;
            }
        }

        float share = outdoors ? day.NightLights : 1f;
        bool burning = share > 0f;
        foreach (PropLight prop in Object.FindObjectsByType<PropLight>(FindObjectsInactive.Exclude))
        {
            Light2D light = prop.LightSource;
            if (light == null)
            {
                continue;
            }

            Record(light);
            light.enabled = burning;
            light.intensity = prop.Intensity * share;
        }

        foreach (TerrainBlendRenderer ground in Object.FindObjectsByType<TerrainBlendRenderer>(FindObjectsInactive.Exclude))
        {
            Light2D glow = ground.GlowLight;
            if (glow == null)
            {
                continue;
            }

            Record(glow);
            glow.enabled = burning;
            glow.intensity = ground.GlowStrength * share;
        }

        bool casts = sun.IsUp && outdoors;
        SunShadowGlobals.Set(casts ? day.Sun : Color.black, casts ? sun.Shadow : Vector2.zero, casts);
        SunShadowGlobals.SetClouds(Vector2.zero, 0f, 0f, 0f, 0f);
        float reflection = outdoors ? Mathf.Lerp(settings.DayReflection, 1f, day.NightLights) : 1f;
        Shader.SetGlobalVector(DayNightPresenter.ReflectionId, new Vector4(sky.r, sky.g, sky.b, reflection));
        Shader.SetGlobalVector(DayNightPresenter.ReliefId, new Vector4(sky.r, sky.g, sky.b, 1f));
        SceneView.RepaintAll();
    }

    /// <summary>
    /// Puts back every light the preview changed, and forgets them.
    /// </summary>
    internal void Restore()
    {
        foreach (LightRecord record in lights)
        {
            if (record.light != null)
            {
                EditorJsonUtility.FromJsonOverwrite(record.state, record.light);
                record.light.enabled = record.enabled;
            }
        }

        lights.Clear();
    }

    /// <summary>
    /// Notes how <paramref name="light"/> is before the preview changes it, once.
    /// </summary>
    private void Record(Light2D light)
    {
        foreach (LightRecord record in lights)
        {
            if (record.light == light)
            {
                return;
            }
        }

        lights.Add(new LightRecord { light = light, enabled = light.enabled, state = EditorJsonUtility.ToJson(light) });
    }

    /// <summary>
    /// Puts the scenes back before one saves, shows the preview again once it has, lights a scene that opens, and
    /// puts the scenes back before play starts, showing the preview again after it.
    /// </summary>
    [InitializeOnLoadMethod]
    private static void Follow()
    {
        EditorSceneManager.sceneSaving -= HandleSaving;
        EditorSceneManager.sceneSaving += HandleSaving;
        EditorSceneManager.sceneSaved -= HandleSaved;
        EditorSceneManager.sceneSaved += HandleSaved;
        EditorSceneManager.sceneOpened -= HandleOpened;
        EditorSceneManager.sceneOpened += HandleOpened;
        EditorApplication.playModeStateChanged -= HandlePlayMode;
        EditorApplication.playModeStateChanged += HandlePlayMode;
    }

    /// <summary>
    /// Puts the scenes back before <paramref name="scene"/> saves.
    /// </summary>
    private static void HandleSaving(Scene scene, string path)
    {
        if (instance.shown)
        {
            instance.Restore();
        }
    }

    /// <summary>
    /// Shows the preview again once <paramref name="scene"/> has saved.
    /// </summary>
    private static void HandleSaved(Scene scene)
    {
        if (instance.shown)
        {
            instance.Apply();
        }
    }

    /// <summary>
    /// Lights <paramref name="scene"/> too once it opens.
    /// </summary>
    private static void HandleOpened(Scene scene, OpenSceneMode mode)
    {
        if (instance.shown)
        {
            instance.Apply();
        }
    }

    /// <summary>
    /// Hides the preview as play is about to start and shows it again once the editor is back in edit mode.
    /// </summary>
    private static void HandlePlayMode(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode && instance.shown)
        {
            instance.resumeAfterPlay = true;
            instance.Hide();
        }
        else if (change == PlayModeStateChange.EnteredEditMode && instance.resumeAfterPlay)
        {
            instance.resumeAfterPlay = false;
            instance.Show(instance.phase);
        }
    }
}
