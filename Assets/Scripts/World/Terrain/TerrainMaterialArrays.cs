using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The texture arrays the terrain shader reads materials from (Docs/TerrainBlending.md): the albedo, cell field and height
/// textures of a list of <see cref="TerrainMaterial"/>s, a slice per material in the list's order, copied on the GPU into
/// arrays as large as the largest material. A <see cref="TerrainBlendRenderer"/> builds them over its own layers'
/// materials, or draws with those built once over its style's layer order and shared by every renderer drawing with the
/// style (<see cref="SharedTerrainArrays"/>), mapping each of its layers onto a slice (<see cref="SliceOf"/>). The arrays
/// keep no copy in memory, are flagged so they are never saved, and are destroyed by <see cref="Dispose"/>.
/// </summary>
public sealed class TerrainMaterialArrays : IDisposable
{
    private readonly TerrainMaterial[] materials;

    /// <summary>
    /// Builds the arrays of <paramref name="arrayMaterials"/>, named after <paramref name="arraysName"/>.
    /// </summary>
    public TerrainMaterialArrays(IReadOnlyList<TerrainMaterial> arrayMaterials, string arraysName)
    {
        materials = new TerrainMaterial[arrayMaterials.Count];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = arrayMaterials[i];
        }

        Albedo = BuildArray(materials, m => m.Albedo, $"{arraysName} Albedo");
        Field = BuildArray(materials, m => m.CellField, $"{arraysName} Cell Field");
        Height = BuildArray(materials, m => m.Height, $"{arraysName} Height");
    }

    public Texture2DArray Albedo { get; private set; }
    public Texture2DArray Field { get; private set; }
    public Texture2DArray Height { get; private set; }

    /// <summary>
    /// The materials, a slice each in this order.
    /// </summary>
    public IReadOnlyList<TerrainMaterial> Materials => materials;

    /// <summary>
    /// Whether the arrays were destroyed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// The slice holding <paramref name="material"/>'s textures, its first place in the list, or -1 when it holds none
    /// (or it is null).
    /// </summary>
    public int SliceOf(TerrainMaterial material)
    {
        for (int i = 0; material != null && i < materials.Length; i++)
        {
            if (materials[i] == material)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Whether every material of <paramref name="wanted"/> has a slice.
    /// </summary>
    public bool HoldsAll(IReadOnlyList<TerrainMaterial> wanted)
    {
        for (int i = 0; i < wanted.Count; i++)
        {
            if (SliceOf(wanted[i]) < 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Destroys the three arrays, immediately outside play mode.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        DestroyRuntime(Albedo);
        DestroyRuntime(Field);
        DestroyRuntime(Height);
        Albedo = null;
        Field = null;
        Height = null;
    }

    /// <summary>
    /// Copies each material's texture of one kind (albedo, cell field or height, picked by <paramref name="kind"/>) into
    /// a slice of a new array as large as the largest material, on the GPU. The array keeps no copy in memory, so it is
    /// uploaded empty and made unreadable before the copies, which a later upload would otherwise overwrite. A slice
    /// whose material has no such texture is left empty: the shader never reads a missing cell field, and the lookup
    /// texture gives a material without a height a height scale of 0.
    /// </summary>
    private static Texture2DArray BuildArray(TerrainMaterial[] arrayMaterials, Func<TerrainMaterial, Texture2D> kind, string arrayName)
    {
        int size = TerrainStyle.PixelsPerCell;
        foreach (TerrainMaterial material in arrayMaterials)
        {
            size = Mathf.Max(size, material.RepeatPixels);
        }

        Texture2DArray array = new(size, size, Mathf.Max(1, arrayMaterials.Length), TextureFormat.RGBA32, false, true)
        {
            name = arrayName,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
            hideFlags = HideFlags.DontSave
        };
        array.Apply(false, true);
        for (int slice = 0; slice < arrayMaterials.Length; slice++)
        {
            Texture2D source = kind(arrayMaterials[slice]);
            if (source != null)
            {
                Graphics.CopyTexture(source, 0, 0, 0, 0, source.width, source.height, array, slice, 0, 0, 0);
            }
        }

        return array;
    }

    /// <summary>
    /// Destroys an object made at runtime, immediately outside play mode.
    /// </summary>
    private static void DestroyRuntime(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(target);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
