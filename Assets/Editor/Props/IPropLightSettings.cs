using UnityEngine;

/// <summary>
/// The light a recipe's props carry (Docs/Lighting.md, Phase 5): its colour, its strength at night, how far it reaches,
/// how far its strength flickers and whether it casts the shadows of walls and props, which the prop writer puts on a
/// <c>Light</c> child of each prefab whose drawing marked a light (<see cref="PropDrawing.MarkLight"/>).
/// </summary>
public interface IPropLightSettings
{
    Color32 LightColour { get; }
    float LightIntensity { get; }
    float LightRadius { get; }
    float LightFlicker { get; }
    bool LightCastsShadows { get; }
}
