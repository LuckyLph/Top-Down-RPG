using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>
/// Writes what a <see cref="PropRecipe"/> bakes (Docs/PropGeneration.md, Output): one sheet,
/// <c>Assets/Sprites/Props/&lt;name&gt;.png</c>, the variants in a row, sliced into <c>&lt;name&gt;_&lt;variant&gt;</c>
/// pivoted on the base pixel, through <see cref="SpriteSheetFiles"/> (an unchanged sheet is not rewritten and slices
/// keep their IDs); then one prefab per variant, <c>Assets/Prefabs/Props/&lt;category&gt;/&lt;name&gt;_&lt;variant&gt;.prefab</c>,
/// a variant of <c>BlockingProp</c> or <c>Prop</c> whose <c>Visuals</c> draw the variant's sprite on the base's
/// rendering layers, which make it cast the sun's shadow, or on the default one alone when the recipe casts none,
/// whose <see cref="PropFade"/> covers its drawing when the recipe fades (and nothing otherwise) and, when it blocks,
/// whose collider is its footprint. A prefab is created once and
/// updated in place afterwards, so its GUID and every placed instance stay, and saved only when it changed. Every
/// variant is baked before anything is written; a recipe is refused when it cannot bake, when a drawing reaches its
/// frame's edge, when a blocking variant touches no ground, when a prefab of a variant past the count is left, or when
/// an existing prefab is a variant of the other base. Writing into the props' folder also makes sure the props' Sprite
/// Atlas is there (<see cref="AtlasPath"/>): it packs the whole folder, every sheet in it, so props draw in few batches.
/// </summary>
public static class PropWriter
{
    public const string SpritesFolder = "Assets/Sprites/Props";
    public const string PrefabsFolder = "Assets/Prefabs/Props";
    public const string PropPath = PrefabsFolder + "/Prop.prefab";
    public const string BlockingPropPath = PrefabsFolder + "/BlockingProp.prefab";
    public const string VisualsName = "Visuals";
    public const string AtlasPath = SpritesFolder + "/Props.spriteatlasv2";
    private const int AtlasPadding = 2;
    private const int AtlasMaxSize = 4096;

    /// <summary>
    /// Bakes and writes <paramref name="recipe"/>'s sheet and prefabs, returning the paths it wrote or changed (none
    /// when nothing changed). Logs an error and writes nothing when the recipe is refused, returning null.
    /// </summary>
    public static IReadOnlyList<string> Generate(PropRecipe recipe)
    {
        IReadOnlyList<string> written = Generate(recipe, SpritesFolder, PrefabsFolder);
        if (written == null || !EnsureAtlas(SpritesFolder, AtlasPath))
        {
            return written;
        }

        List<string> withAtlas = new(written) { AtlasPath };
        return withAtlas;
    }

    /// <summary>
    /// Creates the Sprite Atlas at <paramref name="path"/> packing every sprite in <paramref name="folder"/> unless it is
    /// there, returning whether it was created: pixel art packed as it is imported (point filtered, no mipmaps,
    /// uncompressed), sprites neither rotated nor packed tight, so each keeps its rectangle and pivot.
    /// </summary>
    internal static bool EnsureAtlas(string folder, string path)
    {
        if (AssetDatabase.LoadAssetAtPath<SpriteAtlasAsset>(path) != null || File.Exists(path))
        {
            return false;
        }

        SpriteAtlasAsset atlas = new();
        atlas.Add(new Object[] { AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder) });
        SpriteAtlasAsset.Save(atlas, path);
        AssetDatabase.ImportAsset(path);
        SpriteAtlasImporter importer = (SpriteAtlasImporter)AssetImporter.GetAtPath(path);
        importer.packingSettings = new SpriteAtlasPackingSettings { blockOffset = 1, padding = AtlasPadding, enableRotation = false, enableTightPacking = false };
        importer.textureSettings = new SpriteAtlasTextureSettings { readable = false, generateMipMaps = false, sRGB = true, filterMode = FilterMode.Point };
        TextureImporterPlatformSettings platform = importer.GetPlatformSettings("DefaultTexturePlatform");
        platform.maxTextureSize = AtlasMaxSize;
        platform.format = TextureImporterFormat.RGBA32;
        platform.textureCompression = TextureImporterCompression.Uncompressed;
        platform.crunchedCompression = false;
        importer.SetPlatformSettings(platform);
        importer.includeInBuild = true;
        importer.SaveAndReimport();
        return true;
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
        uint layers = recipe.CastsShadow ? basePrefab.transform.Find(VisualsName).GetComponent<SpriteRenderer>().renderingLayerMask : RenderingLayerMask.defaultRenderingLayerMask.value;
        for (int i = 0; i < variants.Count; i++)
        {
            sprites.TryGetValue(SliceName(recipe.PropName, i), out Sprite sprite);
            string path = PrefabPath(prefabsFolder, recipe, i);
            if (WritePrefab(path, basePrefab, recipe, variants[i], sprite, layers))
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
    /// Writes the sheet of <paramref name="variants"/> at <paramref name="path"/>, returning whether anything changed.
    /// </summary>
    private static bool WriteSheet(PropRecipe recipe, IReadOnlyList<PropVariant> variants, string path)
    {
        Vector2Int frame = recipe.FrameSize;
        int width = frame.x * variants.Count;
        int height = frame.y;
        Color32[] pixels = new Color32[width * height];
        List<SpriteSheetFiles.Slice> slices = new();
        Vector2 pivot = SpriteSheetFiles.Slice.PivotOn(recipe.BasePixel, frame);
        for (int i = 0; i < variants.Count; i++)
        {
            Blit(variants[i].Pixels, frame, pixels, width, i * frame.x, 0);
            slices.Add(new SpriteSheetFiles.Slice(SliceName(recipe.PropName, i), new Rect(i * frame.x, 0, frame.x, frame.y), pivot));
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
    private static bool WritePrefab(string path, GameObject basePrefab, PropRecipe recipe, PropVariant variant, Sprite sprite, uint layers)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null && !Apply(existing, recipe, variant, sprite, layers, false))
        {
            return false;
        }

        if (existing == null)
        {
            TerrainMaterialWriter.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            instance.name = Path.GetFileNameWithoutExtension(path);
            Apply(instance, recipe, variant, sprite, layers, true);
            PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);
            return true;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        Apply(contents, recipe, variant, sprite, layers, true);
        PrefabUtility.SaveAsPrefabAsset(contents, path);
        PrefabUtility.UnloadPrefabContents(contents);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="root"/>'s sprite, rendering layers, fade or footprint differ from the variant's, setting
    /// them when <paramref name="write"/>.
    /// </summary>
    private static bool Apply(GameObject root, PropRecipe recipe, PropVariant variant, Sprite sprite, uint layers, bool write)
    {
        SpriteRenderer visuals = root.transform.Find(VisualsName).GetComponent<SpriteRenderer>();
        bool changed = visuals.sprite != sprite || visuals.renderingLayerMask != layers;
        if (write)
        {
            visuals.sprite = sprite;
            visuals.renderingLayerMask = layers;
        }

        if (root.TryGetComponent(out PropFade fade))
        {
            Rect cover = recipe.Fades ? CoverOf(variant.Drawn, recipe.BasePixel) : default;
            changed |= fade.Visuals != visuals || fade.Cover != cover;
            if (write)
            {
                fade.Configure(visuals, cover);
            }
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
    /// The box <paramref name="drawn"/>, in frame pixels, covers around the base pixel <paramref name="basePixel"/>, in
    /// units.
    /// </summary>
    private static Rect CoverOf(RectInt drawn, Vector2Int basePixel)
    {
        float unit = PropBaker.PixelsPerCell;
        return new Rect((drawn.x - basePixel.x) / unit, (drawn.y - basePixel.y) / unit, drawn.width / unit, drawn.height / unit);
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
