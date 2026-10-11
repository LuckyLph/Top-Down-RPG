using System;
using UnityEngine;

/// <summary>
/// One ground material a <see cref="TerrainBlendRenderer"/> layer draws (Docs/TerrainBlending.md): its textures (32 px
/// per cell, repeating every few cells), its shape's corner radii, how its chips or stones meet an edge, and its edge
/// over any material the <see cref="TerrainStyle"/> has no pair override for. Read-only at runtime: renderers copy its
/// textures and settings and never write it. In the editor, a terrain material recipe generates its textures and writes
/// their base colour, repeat and cell mode.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Material", fileName = "TerrainMaterial")]
public class TerrainMaterial : ScriptableObject
{
    /// <summary>
    /// What a material's cell field holds and how its chips or stones meet an edge: none, chips drawn whole or left
    /// out, or stones left out or cut along the rim.
    /// </summary>
    public enum CellMode
    {
        None,
        WholeChips,
        CutStones
    }

    [Header("Textures")]
    [Tooltip("The interior: repeat x 32 px square, RGBA32 with sRGB off (the shader treats its bytes as sRGB itself). Written by the material's recipe.")]
    [SerializeField] private Texture2D albedo;
    [Tooltip("Optional: for every pixel, the offset to its chip's or stone's centre (see Docs/Architecture.md, World and areas). Same size and format as the albedo. Needed unless Cell Mode is None. Written by the material's recipe.")]
    [SerializeField] private Texture2D cellField;
    [Tooltip("Optional: the height the soft look uses (Phase 2). Same size and format as the albedo. Written by the material's recipe.")]
    [SerializeField] private Texture2D height;
    [Tooltip("Cells the textures cover before they repeat. Written by the material's recipe.")]
    [SerializeField, Min(1)] private int repeatCells = 4;
    [Header("Shape")]
    [Tooltip("Radius in pixels of an outer corner. Under half a cell, so edges stay straight across cell borders. Materials whose edges meet should share their radii, or the lower one shows at corners.")]
    [SerializeField, Range(0f, 15.5f)] private float outerRadius = 14f;
    [Tooltip("Radius in pixels of an inner corner's fillet. Under half a cell.")]
    [SerializeField, Range(0f, 15.5f)] private float innerRadius = 11f;
    [Header("Chips")]
    [Tooltip("What the cell field holds: nothing, whole chips or cut stones. Written by the material's recipe.")]
    [SerializeField] private CellMode cellMode = CellMode.None;
    [Tooltip("Colour under the chips or stones, shown where one is left out or cut; the cell field's blue channel shades it per pixel. Written by the material's recipe with its textures.")]
    [SerializeField] private Color baseColour = Color.gray;
    [Tooltip("Whole chips: a chip is drawn only where all of it (as far as its field's reach) lies at least this many pixels inside the rim, else left out. Cut stones: a stone is drawn only where its centre lies at least this many pixels inside the rim; negative keeps stones whose centre lies in the rim, cut along its line.")]
    [SerializeField, Range(-4f, 6f)] private float keepMargin = 1f;
    [Tooltip("Cut stones: width in pixels of the base colour line between the rim and a stone cut along it.")]
    [SerializeField, Range(0f, 3f)] private float cutGap = 0.5f;
    [Tooltip("Cut stones: how much the light changes a stone's colour along its cut, in 0-255 steps.")]
    [SerializeField, Range(0f, 64f)] private float cutBevel = 18f;
    [Header("Edge")]
    [Tooltip("How this material's edge is drawn over any material, or over nothing, without a pair override in the style.")]
    [SerializeField] private TerrainStyle.EdgeSettings edge = new();

    public Texture2D Albedo => albedo;
    public Texture2D CellField => cellField;
    public Texture2D Height => height;
    public int RepeatCells => repeatCells;
    public int RepeatPixels => repeatCells * TerrainStyle.PixelsPerCell;
    public float OuterRadius => outerRadius;
    public float InnerRadius => innerRadius;
    public CellMode Cells => cellMode;
    public Color BaseColour => baseColour;
    public float KeepMargin => keepMargin;
    public float CutGap => cutGap;
    public float CutBevel => cutBevel;
    public TerrainStyle.EdgeSettings Edge => edge;

#if UNITY_EDITOR
    internal event Action Validated;
#endif

    /// <summary>
    /// Creates a material in memory with its textures, chips and edge, keeping the default corner radii, for tests.
    /// </summary>
    internal static TerrainMaterial Create(string materialName, Texture2D albedo, Texture2D cellField, int repeatCells, CellMode cellMode, Color baseColour, TerrainStyle.EdgeSettings edge)
    {
        TerrainMaterial material = CreateInstance<TerrainMaterial>();
        material.name = materialName;
        material.albedo = albedo;
        material.cellField = cellField;
        material.repeatCells = repeatCells;
        material.cellMode = cellMode;
        material.baseColour = baseColour;
        material.edge = edge;
        return material;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Tells the renderers using this material to rebuild, as an inspector change does; the material recipe calls it
    /// after rewriting the textures, whose references may not have changed.
    /// </summary>
    internal void NotifyChanged()
    {
        Validated?.Invoke();
    }

    /// <summary>
    /// Tells the renderers using this material to rebuild after an inspector change.
    /// </summary>
    private void OnValidate()
    {
        Validated?.Invoke();
    }
#endif
}
