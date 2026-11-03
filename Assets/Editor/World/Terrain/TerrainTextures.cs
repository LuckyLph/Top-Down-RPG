using UnityEngine;

/// <summary>
/// What a <see cref="TerrainMaterialRecipe"/> generates: the pixels of a material's square textures, row by row from
/// the bottom like <see cref="Texture2D.SetPixels32"/>, with the base colour and cell mode the material needs to read
/// them, for an animated material the glint and sparkle colours its height texture's phases light up with, and for a
/// material with relief how its stones are bevelled (Docs/Lighting.md, Phase 11). The cell field, when there is one,
/// keeps the contract the terrain shader reads (Docs/Architecture.md, World and areas).
/// Plain data: nothing here touches an asset.
/// </summary>
public sealed class TerrainTextures
{
    /// <summary>
    /// Holds textures of <paramref name="size"/> pixels square; <paramref name="cellField"/> is null for a material
    /// without chips or stones.
    /// </summary>
    public TerrainTextures(int size, Color32 baseColour, TerrainMaterial.CellMode cellMode, Color32[] albedo, Color32[] cellField, Color32[] height, Color32? glintColour = null, Color32? sparkleColour = null, TerrainMaterial.BevelRelief relief = default)
    {
        Relief = relief;
        Size = size;
        BaseColour = baseColour;
        CellMode = cellMode;
        Albedo = albedo;
        CellField = cellField;
        Height = height;
        GlintColour = glintColour;
        SparkleColour = sparkleColour;
    }

    public int Size { get; }
    public Color32 BaseColour { get; }
    public TerrainMaterial.CellMode CellMode { get; }
    public Color32[] Albedo { get; }
    public Color32[] CellField { get; }
    public Color32[] Height { get; }
    public Color32? GlintColour { get; }
    public Color32? SparkleColour { get; }
    public TerrainMaterial.BevelRelief Relief { get; }
}
