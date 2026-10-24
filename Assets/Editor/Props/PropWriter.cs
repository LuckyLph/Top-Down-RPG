using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes what a <see cref="PropRecipe"/> bakes (Docs/PropGeneration.md, Output): one sheet,
/// <c>Assets/Sprites/Props/&lt;name&gt;.png</c>, the variants in a row over a row of their shadows, sliced into
/// <c>&lt;name&gt;_&lt;variant&gt;</c> pivoted on the base pixel and <c>&lt;name&gt;_&lt;variant&gt;_Shadow</c> pivoted
/// on the shadow frame's base pixel, through <see cref="SpriteSheetFiles"/> (an unchanged sheet is not rewritten and
/// slices keep their IDs); then one prefab per variant, <c>Assets/Prefabs/Props/&lt;category&gt;/&lt;name&gt;_&lt;variant&gt;.prefab</c>,
/// a variant of <c>BlockingProp</c> or <c>Prop</c> whose <c>Visuals</c> draw the variant's sprite, whose
/// <c>Shadow</c> draws its shadow and, when it blocks, whose collider is its footprint. A prefab is created once and
/// updated in place afterwards, so its GUID and every placed instance stay, and saved only when it changed. Every
/// variant is baked before anything is written; a recipe is refused when it cannot bake, when a drawing reaches its
/// frame's edge, when a blocking variant touches no ground, when a prefab of a variant past the count is left, or when
/// an existing prefab is a variant of the other base.
/// </summary>
public static class PropWriter
{
    public const string SpritesFolder = "Assets/Sprites/Props";
    public const string PrefabsFolder = "Assets/Prefabs/Props";
    public const string PropPath = PrefabsFolder + "/Prop.prefab";
    public const string BlockingPropPath = PrefabsFolder + "/BlockingProp.prefab";
    public const string VisualsName = "Visuals";
    public const string ShadowName = "Shadow";

    /// <summary>
    /// Bakes and writes <paramref name="recipe"/>'s sheet and prefabs, returning the paths it wrote or changed (none
    /// when nothing changed). Logs an error and writes nothing when the recipe is refused, returning null.
    /// </summary>
    public static IReadOnlyList<string> Generate(PropRecipe recipe)
    {
        return Generate(recipe, SpritesFolder, PrefabsFolder);
    }

    /// <summary>
    /// <see cref="Generate(PropRecipe)"/> into other folders, for tests: the sheet in <paramref name="spritesFolder"/>
    /// and the prefabs in <paramref name="prefabsFolder"/>'s category folders.
    /// </summary>
    internal static IReadOnlyList<string> Generate(PropRecipe recipe, string spritesFolder, string prefabsFolder)
    {
        List<PropVariant> variants = null;
        string problem = recipe.CanGenerate(out string cannot) ? null : cannot;
        if (problem == null)
        {
            variants = PropBaker.BakeAll(recipe);
            problem = Refusal(recipe, variants, prefabsFolder);
        }

        if (problem != null)
        {
            Debug.LogError($"{recipe.name}: {problem}", recipe);
            return null;
        }

        List<string> written = new();
        string sheet = SheetPath(spritesFolder, recipe.PropName);
        TerrainMaterialWriter.EnsureFolder(spritesFolder);
        if (WriteSheet(recipe, variants, sheet))
        {
            written.Add(sheet);
        }

        Dictionary<string, Sprite> sprites = SpriteSheetFiles.SpritesByName(sheet);
        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(recipe.Blocking ? BlockingPropPath : PropPath);
        for (int i = 0; i < variants.Count; i++)
        {
            sprites.TryGetValue(SliceName(recipe.PropName, i), out Sprite sprite);
            Sprite shadow = null;
            if (recipe.CastsShadow)
            {
                sprites.TryGetValue(ShadowSliceName(recipe.PropName, i), out shadow);
            }

            string path = PrefabPath(prefabsFolder, recipe, i);
            if (WritePrefab(path, basePrefab, recipe, variants[i], sprite, shadow))
            {
                written.Add(path);
            }
        }

        return written;
    }

    /// <summary>
    /// Why <paramref name="recipe"/>'s baked <paramref name="variants"/> cannot be written with its prefabs in
    /// <paramref name="prefabsFolder"/>, or null when they can.
    /// </summary>
    internal static string Refusal(PropRecipe recipe, IReadOnlyList<PropVariant> variants, string prefabsFolder)
    {
        for (int i = 0; i < variants.Count; i++)
        {
            if (variants[i].AtEdge)
            {
                return $"variant {i}'s drawing reaches its frame's edge: make the frame larger or move the base pixel.";
            }

            if (recipe.Blocking && !variants[i].HasFootprint)
            {
                return $"variant {i} blocks but touches no ground.";
            }
        }

        string folder = CategoryFolder(prefabsFolder, recipe);
        if (AssetDatabase.IsValidFolder(folder))
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (VariantIndex(recipe.PropName, Path.GetFileNameWithoutExtension(path)) >= recipe.VariantCount)
                {
                    return $"{path} is a variant past the count of {recipe.VariantCount}; placed props may use it, so the count cannot drop below it.";
                }
            }
        }

        GameObject expected = AssetDatabase.LoadAssetAtPath<GameObject>(recipe.Blocking ? BlockingPropPath : PropPath);
        for (int i = 0; i < variants.Count; i++)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(prefabsFolder, recipe, i));
            if (existing != null && PrefabUtility.GetCorrespondingObjectFromSource(existing) != expected)
            {
                return $"{AssetDatabase.GetAssetPath(existing)} is not a variant of {expected.name}; blocking cannot change once its prefabs exist.";
            }
        }

        return null;
    }

    /// <summary>
    /// The path of the sheet of the props named <paramref name="propName"/> in <paramref name="folder"/>.
    /// </summary>
    public static string SheetPath(string folder, string propName)
    {
        return $"{folder}/{propName}.png";
    }

    /// <summary>
    /// The path of variant <paramref name="index"/>'s prefab under <paramref name="prefabsFolder"/>.
    /// </summary>
    public static string PrefabPath(string prefabsFolder, PropRecipe recipe, int index)
    {
        return $"{CategoryFolder(prefabsFolder, recipe)}/{SliceName(recipe.PropName, index)}.prefab";
    }

    /// <summary>
    /// The name of variant <paramref name="index"/>'s slice, which its prefab is named after too.
    /// </summary>
    public static string SliceName(string propName, int index)
    {
        return $"{propName}_{index}";
    }

    /// <summary>
    /// The name of variant <paramref name="index"/>'s shadow slice.
    /// </summary>
    public static string ShadowSliceName(string propName, int index)
    {
        return $"{propName}_{index}_{ShadowName}";
    }

    /// <summary>
    /// Writes the sheet of <paramref name="variants"/> at <paramref name="path"/>, returning whether anything changed.
    /// </summary>
    private static bool WriteSheet(PropRecipe recipe, IReadOnlyList<PropVariant> variants, string path)
    {
        Vector2Int frame = recipe.FrameSize;
        Vector2Int shadowFrame = recipe.CastsShadow ? PropBaker.ShadowFrameSize(recipe) : Vector2Int.zero;
        int width = frame.x * variants.Count;
        int height = frame.y + shadowFrame.y;
        Color32[] pixels = new Color32[width * height];
        List<SpriteSheetFiles.Slice> slices = new();
        Vector2 pivot = SpriteSheetFiles.Slice.PivotOn(recipe.BasePixel, frame);
        for (int i = 0; i < variants.Count; i++)
        {
            Blit(variants[i].Pixels, frame, pixels, width, i * frame.x, shadowFrame.y);
            slices.Add(new SpriteSheetFiles.Slice(SliceName(recipe.PropName, i), new Rect(i * frame.x, shadowFrame.y, frame.x, frame.y), pivot));
        }

        if (recipe.CastsShadow)
        {
            Vector2 shadowPivot = SpriteSheetFiles.Slice.PivotOn(PropBaker.ShadowBasePixel(recipe), shadowFrame);
            for (int i = 0; i < variants.Count; i++)
            {
                Blit(variants[i].Shadow, shadowFrame, pixels, width, i * frame.x, 0);
                slices.Add(new SpriteSheetFiles.Slice(ShadowSliceName(recipe.PropName, i), new Rect(i * frame.x, 0, shadowFrame.x, shadowFrame.y), shadowPivot));
            }
        }

        return SpriteSheetFiles.WriteSheet(path, width, height, pixels, slices);
    }

    /// <summary>
    /// Copies <paramref name="source"/>, rows from the bottom, into <paramref name="target"/> with its bottom left at
    /// (<paramref name="x"/>, <paramref name="y"/>).
    /// </summary>
    private static void Blit(Color32[] source, Vector2Int size, Color32[] target, int targetWidth, int x, int y)
    {
        for (int row = 0; row < size.y; row++)
        {
            System.Array.Copy(source, row * size.x, target, (y + row) * targetWidth + x, size.x);
        }
    }

    /// <summary>
    /// Creates variant prefab <paramref name="path"/> from <paramref name="basePrefab"/>, or updates it in place when
    /// it differs, returning whether it was written.
    /// </summary>
    private static bool WritePrefab(string path, GameObject basePrefab, PropRecipe recipe, PropVariant variant, Sprite sprite, Sprite shadow)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null && !Apply(existing, recipe, variant, sprite, shadow, false))
        {
            return false;
        }

        if (existing == null)
        {
            TerrainMaterialWriter.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            instance.name = Path.GetFileNameWithoutExtension(path);
            Apply(instance, recipe, variant, sprite, shadow, true);
            PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);
            return true;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        Apply(contents, recipe, variant, sprite, shadow, true);
        PrefabUtility.SaveAsPrefabAsset(contents, path);
        PrefabUtility.UnloadPrefabContents(contents);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="root"/>'s sprite, shadow or footprint differ from the variant's, setting them when
    /// <paramref name="write"/>.
    /// </summary>
    private static bool Apply(GameObject root, PropRecipe recipe, PropVariant variant, Sprite sprite, Sprite shadow, bool write)
    {
        SpriteRenderer visuals = root.transform.Find(VisualsName).GetComponent<SpriteRenderer>();
        SpriteRenderer shadowRenderer = root.transform.Find(ShadowName).GetComponent<SpriteRenderer>();
        bool changed = visuals.sprite != sprite || shadowRenderer.sprite != shadow;
        if (write)
        {
            visuals.sprite = sprite;
            shadowRenderer.sprite = shadow;
        }

        if (!recipe.Blocking || !root.TryGetComponent(out BoxCollider2D box))
        {
            return changed;
        }

        RectInt footprint = variant.Footprint;
        float unit = PropBaker.PixelsPerCell;
        Vector2 size = new(footprint.width / unit, footprint.height / unit);
        Vector2 offset = new((footprint.x + footprint.width * 0.5f - recipe.BasePixel.x) / unit, (footprint.y + footprint.height * 0.5f - recipe.BasePixel.y) / unit);
        changed |= box.size != size || box.offset != offset;
        if (write)
        {
            box.size = size;
            box.offset = offset;
        }

        return changed;
    }

    /// <summary>
    /// The folder of <paramref name="recipe"/>'s prefabs under <paramref name="prefabsFolder"/>.
    /// </summary>
    private static string CategoryFolder(string prefabsFolder, PropRecipe recipe)
    {
        return $"{prefabsFolder}/{recipe.Category}";
    }

    /// <summary>
    /// The variant index a prefab named <paramref name="fileName"/> holds of the props named
    /// <paramref name="propName"/>, or -1 when it is not one of them.
    /// </summary>
    private static int VariantIndex(string propName, string fileName)
    {
        string prefix = propName + "_";
        return fileName.StartsWith(prefix, System.StringComparison.Ordinal) && int.TryParse(fileName.Substring(prefix.Length), out int index) && index >= 0 ? index : -1;
    }
}
