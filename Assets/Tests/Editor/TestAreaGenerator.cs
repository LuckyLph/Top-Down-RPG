using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// An <see cref="AreaGenerator"/> for tests, built in memory by <see cref="Create"/> with the terrain layers and style
/// it is given. Its layouts fill one cell per layer, layer i at (i, 0), and nothing else.
/// </summary>
public sealed class TestAreaGenerator : AreaGenerator
{
    private List<TerrainLayer> layers = new();

    public override IReadOnlyList<TerrainLayer> TerrainLayers => layers;

    public override IReadOnlyList<PropScatter> Scatters => new PropScatter[0];

    /// <summary>
    /// A generator named TestGenerator listing <paramref name="terrainLayers"/>, drawn with <paramref name="style"/>;
    /// the caller destroys it.
    /// </summary>
    public static TestAreaGenerator Create(TerrainStyle style, IReadOnlyList<TerrainLayer> terrainLayers)
    {
        TestAreaGenerator generator = CreateInstance<TestAreaGenerator>();
        generator.name = "TestGenerator";
        generator.layers = new List<TerrainLayer>(terrainLayers);
        SerializedObject data = new(generator);
        data.FindProperty("terrainStyle").objectReferenceValue = style;
        data.ApplyModifiedPropertiesWithoutUndo();
        return generator;
    }

    /// <summary>
    /// A layout filling cell (i, 0) of each layer i, whatever the seed; it has no scatter layers.
    /// </summary>
    public override AreaLayout Generate(int layoutSeed, bool scatter)
    {
        AreaLayout layout = new(layers);
        for (int i = 0; i < layers.Count; i++)
        {
            layout.TerrainCells(i).Add(new Vector3Int(i, 0, 0));
        }

        return layout;
    }
}
