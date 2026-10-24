using System;
using UnityEngine;

/// <summary>
/// A stump: a short trunk whose foot flares into the ground, its outline bulging in a few lobes and its broken top
/// tilted and jagged, drawn as a field marched along the view rays; its cut end (where the surface faces up) is wood
/// with rings around the middle, the rest bark, with roots splayed over the ground (<see cref="PropRoots"/>).
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Props/Wood Recipe", fileName = "Stump_Oak")]
public sealed class WoodRecipe : PropRecipe
{
    private const int Lobes = 5;
    private const float JagWaves = 1.6f;
    private const float BoundsReach = 1.6f;
    private const float BoundsBelow = 0.05f;
    private const float BoundsAbove = 0.12f;

    [Header("Stump (cells)")]
    [Tooltip("The stump's radius above its flare.")]
    [SerializeField, Min(0.05f)] private float radius = 0.27f;
    [Tooltip("How high its top stands above the ground.")]
    [SerializeField, Min(0.05f)] private float height = 0.42f;
    [Tooltip("How much wider its foot is, as a share of its radius.")]
    [SerializeField, Min(0f)] private float flare = 0.4f;
    [Tooltip("Over how high the flare narrows.")]
    [SerializeField, Min(0.01f)] private float flareHeight = 0.06f;
    [Tooltip("How far its broken top rises and falls around it.")]
    [SerializeField, Min(0f)] private float jag = 0.08f;
    [Tooltip("How far its top may tilt, in cells of height per cell east or west.")]
    [SerializeField, Min(0f)] private float slant = 0.1f;
    [Tooltip("How much its outline bulges in its lobes, as a share of its radius.")]
    [SerializeField, Range(0f, 0.3f)] private float wobble = 0.05f;
    [Tooltip("Its cut end is where the surface faces up past this.")]
    [SerializeField, Range(0f, 1f)] private float cutFacing = 0.72f;
    [SerializeField] private PropRoots roots = new(4, new Vector2(1.2f, 1.62f), new Vector2(0.33f, 0.12f), 0.72f);

    [Header("Colours")]
    [SerializeField] private ColourRamp bark = Ramp(0x1e140e, 0x3e2a1c, 0x604230, 0x8a6644);
    [Tooltip("The bark's grooves per cell around the stump.")]
    [SerializeField, Min(1f)] private float barkGrooves = 16f;
    [SerializeField] private ColourRamp cutEnd = Ramp(0x3a2618, 0x9a7048, 0xc09464, 0xe0bc86);
    [Tooltip("The cut end's rings per cell from its middle.")]
    [SerializeField, Min(1f)] private float rings = 11f;

    /// <inheritdoc />
    public override void Draw(PropDrawing drawing)
    {
        int barkId = drawing.AddMaterial(new ArtMaterial(bark, pattern: ArtPattern.Bark, patternScale: barkGrooves), true);
        int endId = drawing.AddMaterial(new ArtMaterial(cutEnd, pattern: ArtPattern.Rings, patternScale: rings), false);
        float tilt = drawing.Range(-slant, slant);
        float phase = drawing.Range(0f, 10f);
        Func<Vector3, float> field = q =>
        {
            float across = Mathf.Sqrt(q.x * q.x + q.y * q.y);
            float angle = Mathf.Atan2(q.y, q.x);
            float r = radius * (1f + flare * Mathf.Exp(-Mathf.Max(q.z, 0f) / flareHeight)) * (1f + wobble * Mathf.Sin(angle * Lobes + phase));
            float rim = height + jag * (ArtNoise.Value(new Vector3(angle * JagWaves + phase, 0.5f, 0.5f)) - 0.5f) * 2f + tilt * q.x;
            return Mathf.Max(across - r, q.z - rim);
        };
        float reach = radius * (1f + flare) * BoundsReach;
        Vector3 min = new(-reach, -reach, -BoundsBelow);
        Vector3 max = new(reach, reach, height + jag + BoundsAbove);
        int part = drawing.NewPart();
        drawing.Canvas.Field(field, new Bounds((min + max) * 0.5f, max - min), (position, surfaceNormal) => surfaceNormal.z > cutFacing ? endId : barkId, part);
        roots.Draw(drawing, radius, barkId, part);
    }
}
