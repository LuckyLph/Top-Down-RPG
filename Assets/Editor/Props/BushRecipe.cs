using UnityEngine;

/// <summary>
/// A bush: a crown of leaf clumps (<see cref="PropCrown"/>) sitting on the ground, its lower part sunk into it.
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

    [Header("Colours")]
    [SerializeField] private ColourRamp leaves = Ramp(0x12301c, 0x2a5e34, 0x438a3e, 0x86b850);

    /// <inheritdoc />
    public override void Draw(PropDrawing drawing)
    {
        int leafId = drawing.AddMaterial(new ArtMaterial(leaves), true);
        int coreId = drawing.AddMaterial(new ArtMaterial(leaves, toneOffset: -1), true);
        float rx = width * 0.5f;
        float rz = height * 0.5f / ArtCanvas.CosPitch;
        crown.Draw(drawing, new Vector3(0f, 0f, rz * lift), new Vector3(rx, rx * depth, rz), leafId, coreId);
    }
}
