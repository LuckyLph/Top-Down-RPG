using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// How a world is lit (Docs/Lighting.md, Phase 3): outdoors its sky follows the day and night, indoors it keeps the
/// light it was authored with.
/// </summary>
public enum WorldLightingProfile
{
    Outdoor,
    Indoor
}

/// <summary>
/// Marks the object in a world's root scene that holds the world's global lights. Only the world the local player is in
/// shows its lighting, since URP lights everything with one global light per sorting layer whichever world it is in, and
/// logs an error whenever a second one is enabled. So in play mode the lights start hidden: this hides its object as
/// the root scene loads, before the lights under it are enabled, and <see cref="LocalWorldPresentation"/> shows it once
/// it is the world on screen. Its profile says whether the world's sky follows the day (<see cref="ApplySky"/>, from
/// <see cref="DayNightPresenter"/>) or keeps its authored light.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-1000)]
public class WorldLighting : MonoBehaviour
{
    [SerializeField, Tooltip("Outdoor: the global lights under this object take the day's sky. Indoor: they keep the light they were authored with.")]
    private WorldLightingProfile profile = WorldLightingProfile.Outdoor;

    private List<Light2D> globalLights;

    public WorldLightingProfile Profile => profile;

    /// <summary>
    /// Hides the lights until the presentation shows them, so a world loading beside the one on screen never enables a
    /// second global light.
    /// </summary>
    private void Awake()
    {
        SetShown(false);
    }

    /// <summary>
    /// Shows or hides the world's lights.
    /// </summary>
    public void SetShown(bool shown)
    {
        if (gameObject.activeSelf != shown)
        {
            gameObject.SetActive(shown);
        }
    }

    /// <summary>
    /// An outdoor world's global lights take <paramref name="sky"/>, at an intensity of 1; an indoor world's are left as
    /// they are.
    /// </summary>
    public void ApplySky(Color sky)
    {
        if (profile != WorldLightingProfile.Outdoor)
        {
            return;
        }

        List<Light2D> lights = GlobalLights();
        for (int i = 0; i < lights.Count; i++)
        {
            Light2D light = lights[i];
            if (light == null)
            {
                continue;
            }

            if (light.color != sky)
            {
                light.color = sky;
            }

            if (light.intensity != 1f)
            {
                light.intensity = 1f;
            }
        }
    }

    /// <summary>
    /// Sets the profile in code, for tests that build a world's lighting without its scene.
    /// </summary>
    internal void Configure(WorldLightingProfile lightingProfile)
    {
        profile = lightingProfile;
        globalLights = null;
    }

    /// <summary>
    /// The global lights under this object, found the first time and kept.
    /// </summary>
    private List<Light2D> GlobalLights()
    {
        if (globalLights == null)
        {
            globalLights = new List<Light2D>();
            foreach (Light2D light in GetComponentsInChildren<Light2D>(true))
            {
                if (light.lightType == Light2D.LightType.Global)
                {
                    globalLights.Add(light);
                }
            }
        }

        return globalLights;
    }
}
