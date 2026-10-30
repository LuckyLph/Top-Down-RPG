using System;
using UnityEngine;

/// <summary>
/// The light a frame of a <see cref="CharacterAnimationClip"/> casts on what is around it (Docs/Lighting.md, Phase 2):
/// where the glow drawn on the frame shines from, in units from the feet on the screen, and how strongly, 0 (no light)
/// to 1 (the clip's brightest). The character baker writes it from where it draws a cast's light; nothing changes it
/// at runtime.
/// </summary>
[Serializable]
public struct CharacterFrameLight
{
    [SerializeField] private Vector2 offset;
    [SerializeField, Range(0f, 1f)] private float strength;

    /// <summary>
    /// Creates the light of a frame shining from <paramref name="lightOffset"/> at <paramref name="lightStrength"/>.
    /// </summary>
    public CharacterFrameLight(Vector2 lightOffset, float lightStrength)
    {
        offset = lightOffset;
        strength = Mathf.Clamp01(lightStrength);
    }

    public Vector2 Offset => offset;
    public float Strength => strength;

    /// <summary>
    /// Whether the frame casts any light.
    /// </summary>
    public bool IsLit => strength > 0f;
}
