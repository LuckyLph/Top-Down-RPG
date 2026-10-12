using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds terrain styles and materials in memory for tests, with <see cref="TerrainStyle.Create"/>,
/// <see cref="TerrainMaterial.Create"/> and fresh textures (albedo, height and, with chips or stones, cell field),
/// using the project's terrain shader, which is only read. Every object it creates is added to the caller's list, for
/// the caller to destroy.
/// </summary>
public static class TestTerrainStyle
{
    public const string ShaderPath = "Assets/Shaders/Terrain/TerrainBlend.shadergraph";

    /// <summary>
    /// A style with the project's terrain shader and <paramref name="pairOverrides"/>, named TerrainStyle.
    /// </summary>
    public static TerrainStyle Create(List<Object> created, params TerrainStyle.PairOverride[] pairOverrides)
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        Assert.That(shader, Is.Not.Null);
        TerrainStyle style = TerrainStyle.Create(shader, pairOverrides);
        style.name = "TerrainStyle";
        created.Add(style);
        return style;
    }

    /// <summary>
    /// Plain grass, gravel (whole chips) and stone (cut stones) materials, in that order.
    /// </summary>
    public static TerrainMaterial[] PlainMaterials(List<Object> created)
    {
        return new[]
        {
            PlainMaterial("Grass", TerrainMaterial.CellMode.None, created),
            PlainMaterial("Gravel", TerrainMaterial.CellMode.WholeChips, created),
            PlainMaterial("Stone", TerrainMaterial.CellMode.CutStones, created)
        };
    }

    /// <summary>
    /// A material named <paramref name="name"/> with a fresh 128 px albedo and height (flat, at half height), a fresh
    /// cell field when <paramref name="mode"/> needs one, and a default edge.
    /// </summary>
    public static TerrainMaterial PlainMaterial(string name, TerrainMaterial.CellMode mode, List<Object> created)
    {
        Texture2D albedo = CreateTexture(name + " albedo", new Color32(90, 140, 60, 255), created);
        Texture2D field = mode == TerrainMaterial.CellMode.None ? null : CreateTexture(name + " field", new Color32(128, 128, 128, 0), created);
        Texture2D height = CreateTexture(name + " height", new Color32(128, 128, 0, 255), created);
        TerrainMaterial material = TerrainMaterial.Create(name, albedo, field, 4, mode, Color.gray, new TerrainStyle.EdgeSettings(), height);
        created.Add(material);
        return material;
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
