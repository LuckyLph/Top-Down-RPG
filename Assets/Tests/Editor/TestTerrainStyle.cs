using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds terrain styles in memory for tests, with <see cref="TerrainStyle.Create"/> and fresh textures, using the
/// project's terrain shader, which is only read. Every object it creates is added to the caller's list, for the caller
/// to destroy.
/// </summary>
public static class TestTerrainStyle
{
    public const string ShaderPath = "Assets/Shaders/Terrain/TerrainBlend.shadergraph";

    /// <summary>
    /// A style of plain grass, gravel (whole chips) and stone (cut stones) materials, as materials 0, 1 and 2.
    /// </summary>
    public static TerrainStyle Create(List<Object> created)
    {
        return Create(created, PlainMaterial("Grass", TerrainStyle.CellMode.None, created),
            PlainMaterial("Gravel", TerrainStyle.CellMode.WholeChips, created),
            PlainMaterial("Stone", TerrainStyle.CellMode.CutStones, created));
    }

    /// <summary>
    /// A style of <paramref name="materials"/>, named TerrainStyle.
    /// </summary>
    public static TerrainStyle Create(List<Object> created, params TerrainStyle.MaterialSettings[] materials)
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        Assert.That(shader, Is.Not.Null);
        TerrainStyle style = TerrainStyle.Create(shader, materials);
        style.name = "TerrainStyle";
        created.Add(style);
        return style;
    }

    /// <summary>
    /// A material named <paramref name="name"/> with a fresh 128 px albedo, a fresh cell field when
    /// <paramref name="mode"/> needs one, and a default edge.
    /// </summary>
    public static TerrainStyle.MaterialSettings PlainMaterial(string name, TerrainStyle.CellMode mode, List<Object> created)
    {
        Texture2D albedo = CreateTexture(name + " albedo", new Color32(90, 140, 60, 255), created);
        Texture2D field = mode == TerrainStyle.CellMode.None ? null : CreateTexture(name + " field", new Color32(128, 128, 128, 0), created);
        return new TerrainStyle.MaterialSettings(name, albedo, field, 4, mode, Color.gray, new TerrainStyle.EdgeSettings());
    }

    /// <summary>
    /// A readable 128 px RGBA32 texture with sRGB off, filled with <paramref name="colour"/>.
    /// </summary>
    public static Texture2D CreateTexture(string name, Color32 colour, List<Object> created)
    {
        Texture2D texture = new(128, 128, TextureFormat.RGBA32, false, true) { name = name };
        Color32[] pixels = new Color32[128 * 128];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = colour;
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        created.Add(texture);
        return texture;
    }
}
