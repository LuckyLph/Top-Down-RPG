using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A bush: a crown of leaf clumps (<see cref="PropCrown"/>) sitting on the ground, its lower part sunk into it, its
/// leaves optionally patterned, dotted with flowers (<see cref="PropFlowers"/>), poked through by bare twigs
/// (<see cref="PropTwigs"/>) or hung with strands of leaves (<see cref="PropStrands"/>, a willow's).
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Props/Bush Recipe", fileName = "Bush_Green")]
public sealed class BushRecipe : PropRecipe
{
    [Header("Size (cells)")]
    [Tooltip("The bush's width.")]
    [SerializeField, Min(0.2f)] private float width = 1.2f;
    [Tooltip("The bush's height on screen, before its clumps.")]
    [SerializeField, Min(0.2f)] private float height = 1.1f;
    [Tooltip("The bush's depth, as a share of its width.")]
    [SerializeField, Range(0.3f, 1f)] private float depth = 0.85f;
    [Tooltip("How high its middle sits, as a share of its height radius; the rest sinks into the ground.")]
    [SerializeField, Range(0f, 1f)] private float lift = 0.7f;

    [Header("Crown")]
    [SerializeField] private PropCrown crown = new(12, new Vector2(0.15f, 0.27f), 0.45f, new Vector2(0.78f, 0.95f));
    [SerializeField] private PropFlowers flowers = new();
    [SerializeField] private PropTwigs twigs = new();
    [SerializeField] private PropStrands strands = new();

    [Header("Colours")]
    [SerializeField] private ColourRamp leaves = Ramp(0x12301c, 0x2a5e34, 0x438a3e, 0x86b850);
    [Tooltip("A pattern over the leaves: Bark streaks them up and down, Speckle dots them.")]
    [SerializeField] private ArtPattern leafPattern = ArtPattern.None;
    [Tooltip("The leaf pattern's streaks per cell across, or its share of speckled pixels.")]
    [SerializeField, Min(0f)] private float leafPatternScale = 16f;
    [SerializeField] private ColourRamp twigColours = Ramp(0x1e140e, 0x3e2a1c, 0x604230, 0x8a6644);

    /// <inheritdoc />
    public override void Draw(PropDrawing drawing)
    {
        int leafId = drawing.AddMaterial(new ArtMaterial(leaves, pattern: leafPattern, patternScale: leafPatternScale), true);
        int coreId = drawing.AddMaterial(new ArtMaterial(leaves, toneOffset: -1, pattern: leafPattern, patternScale: leafPatternScale), true);
        float rx = width * 0.5f;
        float rz = height * 0.5f / ArtCanvas.CosPitch;
        Vector3 centre = new(0f, 0f, rz * lift);
        Vector3 radii = new(rx, rx * depth, rz);
        List<PropClump> clumps = new();
        crown.Draw(drawing, centre, radii, leafId, coreId, clumps);
        flowers.Draw(drawing, clumps, leaves.Outline);
        if (strands.Count > 0)
        {
            strands.Draw(drawing, centre, radii, drawing.AddMaterial(new ArtMaterial(leaves, outlined: false, toneHeight: height), true));
        }

        if (twigs.Count > 0)
        {
            twigs.Draw(drawing, centre, radii, drawing.AddMaterial(new ArtMaterial(twigColours, outlined: false), false));
        }
    }
}
