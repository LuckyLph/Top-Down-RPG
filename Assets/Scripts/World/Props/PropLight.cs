using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The light of a light prop, a campfire or a lamp post (Docs/Lighting.md, Phase 5): its Light 2D, off as the prop
/// writer leaves it, its full strength at night and how far its strength flickers either way. It does nothing itself,
/// so a light prop costs nothing per frame of its own: <see cref="PropLighting"/> learns of it from its chunk and burns
/// it by the time of day.
/// </summary>
[DisallowMultipleComponent]
public sealed class PropLight : MonoBehaviour
{
    [SerializeField, Tooltip("The prop's light, off until it burns.")]
    private Light2D lightSource;
    [SerializeField, Min(0f), Tooltip("The light's strength at night.")]
    private float intensity = 1f;
    [SerializeField, Range(0f, 0.5f), Tooltip("How far the light's strength flickers either way, as a share of it.")]
    private float flicker;

    public Light2D LightSource => lightSource;
    public float Intensity => intensity;
    public float Flicker => flicker;

    /// <summary>
    /// Points at <paramref name="light"/>, burning at <paramref name="strength"/> at night and flickering by
    /// <paramref name="share"/>, for the prop writer and tests.
    /// </summary>
    internal void Configure(Light2D light, float strength, float share)
    {
        lightSource = light;
        intensity = strength;
        flicker = share;
    }

    /// <summary>
    /// Points at the Light 2D beside it when it is added in the editor.
    /// </summary>
    private void Reset()
    {
        lightSource = GetComponent<Light2D>();
    }

    /// <summary>
    /// Warns in the editor when the prop has no light.
    /// </summary>
    private void OnValidate()
    {
        if (lightSource == null)
        {
            Debug.LogWarning($"{name} has no light.", this);
        }
    }
}
