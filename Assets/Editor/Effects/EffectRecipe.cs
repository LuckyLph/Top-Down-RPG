using UnityEngine;

/// <summary>
/// An editor-only recipe for a one-shot effect's frames (Docs/Animation.md, Phase 6), drawn by code straight onto the
/// pixel grid at the game's 32 pixels per unit: one sequence of frames per variant and direction, so nothing rotates,
/// scales or tints an effect at runtime. <see cref="EffectSheetWriter"/> bakes it without touching an asset, the same
/// recipe always giving the same pixels, and writes it as a sheet under <c>Assets/Sprites/Effects/</c> with its
/// runtime <see cref="EffectSprites"/>.
/// </summary>
public abstract class EffectRecipe : ScriptableObject
{
    [Tooltip("The sheet's file name under Assets/Sprites/Effects, its slices' prefix and its sprite set's name. Letters, digits and underscores only.")]
    [SerializeField] private string sheetName = "Effect";
    [Tooltip("How long each frame shows, in seconds.")]
    [SerializeField, Min(0.001f)] private float frameSeconds = 0.04f;

    public string SheetName => sheetName;
    public float FrameSeconds => frameSeconds;

    public abstract int FrameWidth { get; }
    public abstract int FrameHeight { get; }
    public abstract int FrameCount { get; }
    public abstract int VariantCount { get; }
    public abstract int DirectionCount { get; }

    /// <summary>
    /// Where a frame's sprite pivots, as a share of the frame from its bottom left.
    /// </summary>
    public abstract Vector2 Pivot { get; }

    /// <summary>
    /// Draws frame <paramref name="frame"/> of <paramref name="variant"/> facing <paramref name="direction"/>
    /// (counterclockwise from east, evenly spaced): <see cref="FrameWidth"/> by <see cref="FrameHeight"/> pixels, rows
    /// from the bottom.
    /// </summary>
    public abstract Color32[] BakeFrame(int variant, int direction, int frame);

    /// <summary>
    /// The colour of the light <paramref name="variant"/> casts on what is around it (Docs/Lighting.md, Phase 2); clear,
    /// the default, for one that casts none.
    /// </summary>
    public virtual Color LightColour(int variant)
    {
        return Color.clear;
    }

    /// <summary>
    /// Whether the recipe can bake; otherwise <paramref name="problem"/> says why.
    /// </summary>
    public virtual bool CanGenerate(out string problem)
    {
        if (string.IsNullOrEmpty(sheetName))
        {
            problem = "Name the sheet.";
            return false;
        }

        foreach (char c in sheetName)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                problem = "The sheet's name may only hold letters, digits and underscores.";
                return false;
            }
        }

        problem = null;
        return true;
    }

    /// <summary>
    /// Sets the name and frame time in code, for the recipes' factories.
    /// </summary>
    protected void SetSheet(string name, float seconds)
    {
        sheetName = name;
        frameSeconds = seconds;
    }
}
