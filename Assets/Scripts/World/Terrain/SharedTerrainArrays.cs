using System;
using System.Collections.Generic;

/// <summary>
/// The texture arrays of each terrain style's layer order (<see cref="TerrainMaterialArrays"/>), shared by every
/// <see cref="TerrainBlendRenderer"/> drawing with the style (Docs/WorldScale.md, Phase 2), so a chunk load builds no
/// arrays. Built when a renderer first asks for its style's (<see cref="Acquire"/>), kept while any renderer uses them,
/// and destroyed with the last one's <see cref="Release"/>, or when the Gameplay scope that registers it is disposed. A
/// style with no layer order has none to share. In the editor an inspector change to a style or to one of its order's
/// materials (their <c>Validated</c> events, which also have the renderers drawing with them rebuild) retires the arrays
/// built for it: the renderers that rebuild get new ones, and the old are destroyed with the last renderer drawing with
/// them. Plain C#: each chunk's scope hands it to the chunk's renderer.
/// </summary>
public sealed class SharedTerrainArrays : IDisposable
{
    private sealed class Entry
    {
        public TerrainStyle Style;
        public TerrainMaterialArrays Arrays;
        public int Users;
#if UNITY_EDITOR
        public bool Retired;
        public Action Retire;
        public TerrainMaterial[] Followed;
#endif
    }

    private readonly Dictionary<TerrainStyle, Entry> current = new();
    private readonly Dictionary<TerrainMaterialArrays, Entry> byArrays = new();
    private bool disposed;

    /// <summary>
    /// How many sets of arrays are alive, current or retired but still drawn with.
    /// </summary>
    internal int AliveCount => byArrays.Count;

    /// <summary>
    /// The arrays of <paramref name="style"/>'s layer order, built now if no renderer holds them, counting the caller
    /// as a user until it calls <see cref="Release"/>; null when the style is null or has no layer order, or once this is
    /// disposed.
    /// </summary>
    public TerrainMaterialArrays Acquire(TerrainStyle style)
    {
        if (disposed || style == null || style.LayerOrder.Count == 0)
        {
            return null;
        }

        if (!current.TryGetValue(style, out Entry entry))
        {
            List<TerrainMaterial> materials = new(style.LayerOrder.Count);
            foreach (TerrainMaterial material in style.LayerOrder)
            {
                if (material != null && !materials.Contains(material))
                {
                    materials.Add(material);
                }
            }

            if (materials.Count == 0)
            {
                return null;
            }

            entry = new Entry { Style = style, Arrays = new TerrainMaterialArrays(materials, $"{style.name} Shared Terrain") };
            current[style] = entry;
            byArrays[entry.Arrays] = entry;
#if UNITY_EDITOR
            Follow(entry);
#endif
        }

        entry.Users++;
        return entry.Arrays;
    }

    /// <summary>
    /// Counts one user less of <paramref name="arrays"/>, from <see cref="Acquire"/>, and destroys them with the last
    /// one. Does nothing for arrays it did not hand out, or once this is disposed.
    /// </summary>
    public void Release(TerrainMaterialArrays arrays)
    {
        if (disposed || arrays == null || !byArrays.TryGetValue(arrays, out Entry entry))
        {
            return;
        }

        entry.Users--;
        if (entry.Users > 0)
        {
            return;
        }

        Destroy(entry);
    }

    /// <summary>
    /// Destroys every set of arrays, whoever still draws with them; later calls hand out none.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        List<Entry> entries = new(byArrays.Values);
        foreach (Entry entry in entries)
        {
            Destroy(entry);
        }

        disposed = true;
    }

    /// <summary>
    /// Stops following the entry's style and materials, forgets it and destroys its arrays.
    /// </summary>
    private void Destroy(Entry entry)
    {
#if UNITY_EDITOR
        StopFollowing(entry);
#endif
        if (current.TryGetValue(entry.Style, out Entry shown) && shown == entry)
        {
            current.Remove(entry.Style);
        }

        byArrays.Remove(entry.Arrays);
        entry.Arrays.Dispose();
    }

#if UNITY_EDITOR
    /// <summary>
    /// Has an inspector change to the entry's style or to one of its materials retire it.
    /// </summary>
    private void Follow(Entry entry)
    {
        entry.Retire = () => Retire(entry);
        entry.Followed = new TerrainMaterial[entry.Arrays.Materials.Count];
        for (int i = 0; i < entry.Followed.Length; i++)
        {
            entry.Followed[i] = entry.Arrays.Materials[i];
            entry.Followed[i].Validated += entry.Retire;
        }

        entry.Style.Validated += entry.Retire;
    }

    /// <summary>
    /// Stops following the entry's style and materials.
    /// </summary>
    private static void StopFollowing(Entry entry)
    {
        if (entry.Retire == null)
        {
            return;
        }

        if (entry.Style != null)
        {
            entry.Style.Validated -= entry.Retire;
        }

        foreach (TerrainMaterial material in entry.Followed)
        {
            if (material != null)
            {
                material.Validated -= entry.Retire;
            }
        }

        entry.Retire = null;
    }

    /// <summary>
    /// Takes the entry out of the current arrays, so the next renderer to ask for its style gets new ones; its arrays
    /// stay until their last user releases them.
    /// </summary>
    private void Retire(Entry entry)
    {
        if (entry.Retired)
        {
            return;
        }

        entry.Retired = true;
        StopFollowing(entry);
        if (current.TryGetValue(entry.Style, out Entry shown) && shown == entry)
        {
            current.Remove(entry.Style);
        }
    }
#endif
}
