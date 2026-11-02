using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.U2D;

/// <summary>
/// Writes what a <see cref="PropRecipe"/> bakes (Docs/PropGeneration.md, Output): one sheet,
/// <c>Assets/Sprites/Props/&lt;name&gt;.png</c>, the variants in a row, sliced into <c>&lt;name&gt;_&lt;variant&gt;</c>
/// pivoted on the base pixel, through <see cref="SpriteSheetFiles"/> (an unchanged sheet is not rewritten and slices
/// keep their IDs); then one prefab per variant, <c>Assets/Prefabs/Props/&lt;category&gt;/&lt;name&gt;_&lt;variant&gt;.prefab</c>,
/// a variant of <c>BlockingProp</c> or <c>Prop</c> whose <c>Visuals</c> draw the variant's sprite on the base's
/// rendering layers, which make it cast the sun's shadow, or on the default one alone when the recipe casts none,
/// whose <see cref="PropFade"/> covers its drawing when the recipe fades (and nothing otherwise) and, when it blocks,
/// whose collider is its footprint and whose <c>Visuals</c>, unless it is a light prop, carry a Shadow Caster 2D over
/// that footprint, off until a burning light wakes it (Docs/Lighting.md, Phase 7). A prop that glows (a light prop, <see cref="LightRecipe"/>, Docs/Lighting.md,
/// Phase 5, or a charred one with embers, Phase 6) also gets the glow of its variants laid out as its sheet,
/// <c>Assets/Textures/Props/&lt;name&gt;_Glow.png</c>, as the sheet's secondary texture <c>_GlowTex</c>, and its sheet
/// goes in <c>Assets/Sprites/GlowingProps</c>, outside the props' atlas, which warns and keeps a mostly empty page when
/// only some of its sprites have a secondary texture; a sheet written before in the other folder is moved there with
/// its slices. A light prop's prefabs get a <c>Light</c> child on the ground under where its drawing marked its light:
/// a point Light 2D, off, casting hard shadows, and the <see cref="PropLight"/> that burns it. A moving flame's sheet and glow hold its frames
/// in rows over the first, which is sliced, imported as full rectangles so a frame reaching past the first's outline
/// still draws, and its prefabs draw with the flames material (<see cref="FlamesMaterialPath"/>), whose
/// <c>_FlameFrames</c> tells the sprite shader how many rows it steps through. A prefab is created once and
/// updated in place afterwards, so its GUID and every placed instance stay, and saved only when it changed. Every
/// variant is baked before anything is written; a recipe is refused when it cannot bake, when a drawing reaches its
/// frame's edge, when a blocking variant touches no ground, when a prefab of a variant past the count is left, or when
/// an existing prefab is a variant of the other base. Writing into the props' folder also makes sure the props' Sprite
/// Atlas is there (<see cref="AtlasPath"/>): it packs the whole folder, every sheet in it, so props draw in few batches.
/// </summary>
public static class PropWriter
{
    public const string SpritesFolder = "Assets/Sprites/Props";
    public const string GlowingSpritesFolder = "Assets/Sprites/GlowingProps";
    public const string PrefabsFolder = "Assets/Prefabs/Props";
    public const string PropPath = PrefabsFolder + "/Prop.prefab";
    public const string BlockingPropPath = PrefabsFolder + "/BlockingProp.prefab";
    public const string VisualsName = "Visuals";
    public const string LightName = "Light";
    public const string GlowFolder = "Assets/Textures/Props";
    public const string GlowTextureName = SpriteSheetFiles.GlowTextureName;
    public const string AtlasPath = SpritesFolder + "/Props.spriteatlasv2";
    public const string FlamesMaterialPath = "Assets/Materials/SpritePixelLitFlames.mat";
    public const string FlameFramesName = "_FlameFrames";
    private const float LightFalloff = 0.5f;
    private static readonly string[] LitSortingLayers = { "Default", "Terrain", "Entities" };
    private static readonly string[] ShadowedSortingLayers = { "Default", "Terrain" };
    private const int ShapeEditorSource = 1;
    private const int AtlasPadding = 2;
    private const int AtlasMaxSize = 4096;

    /// <summary>
    /// Bakes and writes <paramref name="recipe"/>'s sheet and prefabs, returning the paths it wrote or changed (none
    /// when nothing changed). Logs an error and writes nothing when the recipe is refused, returning null.
    /// </summary>
    public static IReadOnlyList<string> Generate(PropRecipe recipe)
    {
        IReadOnlyList<string> written = Generate(recipe, SpritesFolder, GlowingSpritesFolder, PrefabsFolder, GlowFolder, out bool glows);
        if (written == null || glows || !EnsureAtlas(SpritesFolder, AtlasPath))
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
    /// <see cref="Generate(PropRecipe)"/> into other folders, for tests: the sheet in <paramref name="spritesFolder"/>,
    /// its glow in <paramref name="glowFolder"/> (beside the sheet when null; never a folder whose textures import as
    /// sprites) and the prefabs in <paramref name="prefabsFolder"/>'s category folders.
    /// </summary>
    internal static IReadOnlyList<string> Generate(PropRecipe recipe, string spritesFolder, string prefabsFolder, string glowFolder = null)
    {
        return Generate(recipe, spritesFolder, spritesFolder, prefabsFolder, glowFolder, out _);
    }

    /// <summary>
    /// Writes <paramref name="recipe"/>'s sheet in <paramref name="spritesFolder"/>, or in
    /// <paramref name="glowingSpritesFolder"/> when it <paramref name="glows"/>, moved there with its slices from the
    /// other one, its glow in <paramref name="glowFolder"/> and its prefabs in <paramref name="prefabsFolder"/>'s
    /// category folders; returns the paths written, or null when it is refused.
    /// </summary>
    internal static IReadOnlyList<string> Generate(PropRecipe recipe, string spritesFolder, string glowingSpritesFolder, string prefabsFolder, string glowFolder, out bool glows)
    {
        glows = false;
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

        foreach (PropVariant variant in variants)
        {
            glows |= variant.Glows;
        }

        List<string> written = new();
        string folder = glows ? glowingSpritesFolder : spritesFolder;
        string sheet = SheetPath(folder, recipe.PropName);
        TerrainMaterialWriter.EnsureFolder(folder);
        string elsewhere = SheetPath(glows ? spritesFolder : glowingSpritesFolder, recipe.PropName);
        if (elsewhere != sheet && AssetImporter.GetAtPath(elsewhere) != null && AssetImporter.GetAtPath(sheet) == null)
        {
            string moveError = AssetDatabase.MoveAsset(elsewhere, sheet);
            if (!string.IsNullOrEmpty(moveError))
            {
                Debug.LogError($"{recipe.name}: {elsewhere} could not move to {sheet}: {moveError}", recipe);
                return null;
            }

            written.Add(sheet);
        }

        if (WriteSheet(recipe, variants, sheet) && !written.Contains(sheet))
        {
            written.Add(sheet);
        }

        string glow = GlowPath(glowFolder ?? spritesFolder, recipe.PropName);
        if (WriteGlow(recipe, variants, sheet, glow))
        {
            written.Add(glow);
        }

        Dictionary<string, Sprite> sprites = SpriteSheetFiles.SpritesByName(sheet);
        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(recipe.Blocking ? BlockingPropPath : PropPath);
        SpriteRenderer baseVisuals = basePrefab.transform.Find(VisualsName).GetComponent<SpriteRenderer>();
        uint layers = recipe.CastsShadow ? baseVisuals.renderingLayerMask : RenderingLayerMask.defaultRenderingLayerMask.value;
        Material material = baseVisuals.sharedMaterial;
        if (recipe.Frames > 1)
        {
            if (EnsureFlamesMaterial(material, recipe.Frames))
            {
                written.Add(FlamesMaterialPath);
            }

            material = AssetDatabase.LoadAssetAtPath<Material>(FlamesMaterialPath);
        }

        for (int i = 0; i < variants.Count; i++)
        {
            sprites.TryGetValue(SliceName(recipe.PropName, i), out Sprite sprite);
            string path = PrefabPath(prefabsFolder, recipe, i);
            if (WritePrefab(path, basePrefab, recipe, variants[i], sprite, material, layers))
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
    /// The path of the glow of the props named <paramref name="propName"/> in <paramref name="folder"/>.
    /// </summary>
    public static string GlowPath(string folder, string propName)
    {
        return $"{folder}/{propName}_Glow.png";
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
    /// Writes the sheet of <paramref name="variants"/> at <paramref name="path"/>, each frame in a row over the first,
    /// which is sliced, imported as full rectangles when it holds more than one frame; returns whether anything changed.
    /// </summary>
    private static bool WriteSheet(PropRecipe recipe, IReadOnlyList<PropVariant> variants, string path)
    {
        Vector2Int frame = recipe.FrameSize;
        int frames = recipe.Frames;
        int width = frame.x * variants.Count;
        int height = frame.y * frames;
        Color32[] pixels = new Color32[width * height];
        List<SpriteSheetFiles.Slice> slices = new();
        Vector2 pivot = SpriteSheetFiles.Slice.PivotOn(recipe.BasePixel, frame);
        for (int i = 0; i < variants.Count; i++)
        {
            for (int f = 0; f < frames; f++)
            {
                Blit(variants[i].FramePixels[f], frame, pixels, width, i * frame.x, f * frame.y);
            }

            slices.Add(new SpriteSheetFiles.Slice(SliceName(recipe.PropName, i), new Rect(i * frame.x, 0, frame.x, frame.y), pivot));
        }

        bool changed = SpriteSheetFiles.WriteSheet(path, width, height, pixels, slices);
        return WriteMeshType(path, frames > 1) || changed;
    }

    /// <summary>
    /// Imports the sprites of the sheet at <paramref name="path"/> as full rectangles when <paramref name="fullRect"/>,
    /// as tight meshes otherwise; returns whether that changed.
    /// </summary>
    private static bool WriteMeshType(string path, bool fullRect)
    {
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        TextureImporterSettings settings = new();
        importer.ReadTextureSettings(settings);
        SpriteMeshType wanted = fullRect ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
        if (settings.spriteMeshType == wanted)
        {
            return false;
        }

        settings.spriteMeshType = wanted;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
        return true;
    }

    /// <summary>
    /// Creates the flames material at <see cref="FlamesMaterialPath"/>, a copy of <paramref name="lit"/> stepping through
    /// <paramref name="frames"/> rows, unless it is there, or sets its frame count when it differs; returns whether it
    /// was created or changed.
    /// </summary>
    private static bool EnsureFlamesMaterial(Material lit, int frames)
    {
        Material flames = AssetDatabase.LoadAssetAtPath<Material>(FlamesMaterialPath);
        if (flames == null)
        {
            flames = new Material(lit);
            flames.SetFloat(FlameFramesName, frames);
            AssetDatabase.CreateAsset(flames, FlamesMaterialPath);
            return true;
        }

        if (Mathf.Approximately(flames.GetFloat(FlameFramesName), frames))
        {
            return false;
        }

        flames.SetFloat(FlameFramesName, frames);
        EditorUtility.SetDirty(flames);
        AssetDatabase.SaveAssetIfDirty(flames);
        return true;
    }

    /// <summary>
    /// Writes the glow of <paramref name="variants"/> at <paramref name="path"/>, laid out as the sheet with its frames, and makes it the
    /// secondary texture <see cref="GlowTextureName"/> of the sheet at <paramref name="sheet"/>; when no variant glows,
    /// removes both. Returns whether anything changed.
    /// </summary>
    private static bool WriteGlow(PropRecipe recipe, IReadOnlyList<PropVariant> variants, string sheet, string path)
    {
        bool glows = false;
        foreach (PropVariant variant in variants)
        {
            glows |= variant.Glows;
        }

        bool changed = false;
        SecondarySpriteTexture[] wanted = new SecondarySpriteTexture[0];
        if (glows)
        {
            Vector2Int frame = recipe.FrameSize;
            int frames = recipe.Frames;
            int width = frame.x * variants.Count;
            Color32[] pixels = new Color32[width * frame.y * frames];
            for (int i = 0; i < variants.Count; i++)
            {
                for (int f = 0; f < frames; f++)
                {
                    Blit(variants[i].FrameGlow[f], frame, pixels, width, i * frame.x, f * frame.y);
                }
            }

            changed = SpriteSheetFiles.WriteGlow(path, width, frame.y * frames, pixels);
            wanted = new[] { new SecondarySpriteTexture { name = GlowTextureName, texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path) } };
        }
        else if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null)
        {
            AssetDatabase.DeleteAsset(path);
            changed = true;
        }

        return SpriteSheetFiles.SetSecondaryTextures(sheet, wanted) || changed;
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
    private static bool WritePrefab(string path, GameObject basePrefab, PropRecipe recipe, PropVariant variant, Sprite sprite, Material material, uint layers)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null && !Apply(existing, recipe, variant, sprite, material, layers, false))
        {
            return false;
        }

        if (existing == null)
        {
            TerrainMaterialWriter.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            instance.name = Path.GetFileNameWithoutExtension(path);
            Apply(instance, recipe, variant, sprite, material, layers, true);
            PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);
            return true;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        Apply(contents, recipe, variant, sprite, material, layers, true);
        PrefabUtility.SaveAsPrefabAsset(contents, path);
        PrefabUtility.UnloadPrefabContents(contents);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="root"/>'s sprite, material, rendering layers, fade, light, footprint or shadow caster
    /// differ from the variant's, setting them when <paramref name="write"/>.
    /// </summary>
    private static bool Apply(GameObject root, PropRecipe recipe, PropVariant variant, Sprite sprite, Material material, uint layers, bool write)
    {
        SpriteRenderer visuals = root.transform.Find(VisualsName).GetComponent<SpriteRenderer>();
        bool changed = visuals.sprite != sprite || visuals.sharedMaterial != material || visuals.renderingLayerMask != layers;
        if (write)
        {
            visuals.sprite = sprite;
            visuals.sharedMaterial = material;
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

        changed |= ApplyLight(root, recipe as LightRecipe, variant, write);
        RectInt footprint = variant.Footprint;
        float unit = PropBaker.PixelsPerCell;
        Vector2 size = new(footprint.width / unit, footprint.height / unit);
        Vector2 offset = new((footprint.x + footprint.width * 0.5f - recipe.BasePixel.x) / unit, (footprint.y + footprint.height * 0.5f - recipe.BasePixel.y) / unit);
        bool casts = recipe.Blocking && recipe is not LightRecipe && root.TryGetComponent(out BoxCollider2D _);
        changed |= ApplyCaster(visuals, casts ? new Rect(offset - size * 0.5f, size) : null, write);
        if (!recipe.Blocking || !root.TryGetComponent(out BoxCollider2D box))
        {
            return changed;
        }

        changed |= box.size != size || box.offset != offset;
        if (write)
        {
            box.size = size;
            box.offset = offset;
        }

        return changed;
    }

    /// <summary>
    /// Whether <paramref name="visuals"/>' light shadow caster differs from what <paramref name="footprint"/> asks for
    /// (none when null), setting it when <paramref name="write"/> (Docs/Lighting.md, Phase 7): a Shadow Caster 2D, off
    /// until a burning light wakes it (<see cref="PropLighting"/>), its shape the footprint's box in the prop's space,
    /// without URP's edge clipping or trim, casting a light's shadow onto the ground's sorting layers
    /// (<see cref="ShadowedSortingLayers"/>) only, so no sprite darkens in a prop's shadow, and leaving its own sprite lit.
    /// </summary>
    private static bool ApplyCaster(SpriteRenderer visuals, Rect? footprint, bool write)
    {
        visuals.TryGetComponent(out ShadowCaster2D caster);
        if (footprint == null)
        {
            if (caster != null && write)
            {
                Object.DestroyImmediate(caster, true);
            }

            return caster != null;
        }

        bool changed = caster == null;
        if (caster == null)
        {
            if (!write)
            {
                return true;
            }

            caster = visuals.gameObject.AddComponent<ShadowCaster2D>();
        }

        Rect box = footprint.Value;
        Vector3 origin = visuals.transform.localPosition;
        Vector3[] path =
        {
            new Vector3(box.xMin, box.yMin) - origin,
            new Vector3(box.xMax, box.yMin) - origin,
            new Vector3(box.xMax, box.yMax) - origin,
            new Vector3(box.xMin, box.yMax) - origin
        };
        int[] layers = SortingLayerIds(ShadowedSortingLayers);
        SerializedObject serialized = new(caster);
        SerializedProperty shape = serialized.FindProperty("m_ShapePath");
        SerializedProperty sortingLayers = serialized.FindProperty("m_ApplyToSortingLayers");
        SerializedProperty source = serialized.FindProperty("m_ShadowCastingSource");
        SerializedProperty edges = serialized.FindProperty("m_ShadowMesh.m_EdgeProcessing");
        SerializedProperty trim = serialized.FindProperty("m_ShadowMesh.m_TrimEdge");
        changed |= caster.enabled || caster.castingOption != ShadowCaster2D.ShadowCastingOptions.CastShadow || source.intValue != ShapeEditorSource
            || edges.intValue != 0 || trim.floatValue != 0f || !Same(shape, path) || !Same(sortingLayers, layers)
            || serialized.FindProperty("m_ShadowShape2DComponent").objectReferenceValue != null;
        if (!write)
        {
            return changed;
        }

        caster.enabled = false;
        caster.castingOption = ShadowCaster2D.ShadowCastingOptions.CastShadow;
        serialized = new SerializedObject(caster);
        serialized.FindProperty("m_ShadowCastingSource").intValue = ShapeEditorSource;
        shape = serialized.FindProperty("m_ShapePath");
        shape.arraySize = path.Length;
        for (int i = 0; i < path.Length; i++)
        {
            shape.GetArrayElementAtIndex(i).vector3Value = path[i];
        }

        serialized.FindProperty("m_ShapePathHash").intValue = PathHash(path);
        sortingLayers = serialized.FindProperty("m_ApplyToSortingLayers");
        sortingLayers.arraySize = layers.Length;
        for (int i = 0; i < layers.Length; i++)
        {
            sortingLayers.GetArrayElementAtIndex(i).intValue = layers[i];
        }

        serialized.FindProperty("m_ShadowMesh.m_EdgeProcessing").intValue = 0;
        serialized.FindProperty("m_ShadowMesh.m_TrimEdge").floatValue = 0f;
        serialized.FindProperty("m_ShadowMesh.m_LocalBounds").boundsValue = new Bounds(path[0] + (path[2] - path[0]) * 0.5f, path[2] - path[0]);
        serialized.FindProperty("m_ShadowShape2DComponent").objectReferenceValue = null;
        serialized.FindProperty("m_ShadowShape2DProvider").managedReferenceValue = null;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return changed;
    }

    /// <summary>
    /// Whether the array <paramref name="property"/> holds <paramref name="points"/>.
    /// </summary>
    private static bool Same(SerializedProperty property, Vector3[] points)
    {
        if (property.arraySize != points.Length)
        {
            return false;
        }

        for (int i = 0; i < points.Length; i++)
        {
            if (property.GetArrayElementAtIndex(i).vector3Value != points[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the array <paramref name="property"/> holds <paramref name="values"/>.
    /// </summary>
    private static bool Same(SerializedProperty property, int[] values)
    {
        if (property.arraySize != values.Length)
        {
            return false;
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (property.GetArrayElementAtIndex(i).intValue != values[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A hash of a caster's shape, never zero, so a caster builds its shadow from the shape the first time it wakes.
    /// </summary>
    private static int PathHash(Vector3[] path)
    {
        unchecked
        {
            int hash = 17;
            foreach (Vector3 point in path)
            {
                hash = hash * 31 + Mathf.RoundToInt(point.x * 1024f);
                hash = hash * 31 + Mathf.RoundToInt(point.y * 1024f);
            }

            return hash == 0 ? 1 : hash;
        }
    }

    /// <summary>
    /// Whether <paramref name="root"/>'s <c>Light</c> child differs from what a light prop's variant asks for (none for
    /// any other prop), setting it when <paramref name="write"/>: a point Light 2D on blend style 0 and the
    /// <c>Default</c>, <c>Terrain</c> and <c>Entities</c> sorting layers, casting shadows at full strength with hard edges
    /// (Docs/Lighting.md, Phase 7), off, where the variant's light stands, in the recipe's colour, strength and radius,
    /// with the <see cref="PropLight"/> that burns it.
    /// </summary>
    private static bool ApplyLight(GameObject root, LightRecipe recipe, PropVariant variant, bool write)
    {
        Transform child = root.transform.Find(LightName);
        if (recipe == null || !variant.HasLight)
        {
            if (child != null && write)
            {
                Object.DestroyImmediate(child.gameObject, true);
            }

            return child != null;
        }

        bool changed = child == null;
        if (child == null)
        {
            if (!write)
            {
                return true;
            }

            child = new GameObject(LightName).transform;
            child.SetParent(root.transform, false);
            child.gameObject.AddComponent<Light2D>();
            child.gameObject.AddComponent<PropLight>();
        }

        Light2D light = child.GetComponent<Light2D>();
        PropLight propLight = child.GetComponent<PropLight>();
        Vector3 position = new(variant.LightOffset.x, variant.LightOffset.y, 0f);
        Color colour = recipe.LightColour;
        int[] layers = SortingLayerIds(LitSortingLayers);
        changed |= child.localPosition != position || light.lightType != Light2D.LightType.Point || light.blendStyleIndex != 0 || light.color != colour
            || !Mathf.Approximately(light.intensity, recipe.LightIntensity) || !Mathf.Approximately(light.pointLightOuterRadius, recipe.LightRadius)
            || light.pointLightInnerRadius != 0f || !Mathf.Approximately(light.falloffIntensity, LightFalloff) || !light.shadowsEnabled
            || light.shadowIntensity != 1f || light.shadowSoftness != 0f || light.enabled
            || !SameLayers(light.targetSortingLayers, layers) || propLight.LightSource != light || !Mathf.Approximately(propLight.Intensity, recipe.LightIntensity)
            || !Mathf.Approximately(propLight.Flicker, recipe.LightFlicker);
        if (write)
        {
            child.localPosition = position;
            light.lightType = Light2D.LightType.Point;
            light.blendStyleIndex = 0;
            light.color = colour;
            light.intensity = recipe.LightIntensity;
            light.pointLightInnerRadius = 0f;
            light.pointLightOuterRadius = recipe.LightRadius;
            light.falloffIntensity = LightFalloff;
            light.shadowsEnabled = true;
            light.shadowIntensity = 1f;
            light.shadowSoftness = 0f;
            light.targetSortingLayers = layers;
            light.enabled = false;
            propLight.Configure(light, recipe.LightIntensity, recipe.LightFlicker);
        }

        return changed;
    }

    /// <summary>
    /// The IDs of the sorting layers named <paramref name="names"/>.
    /// </summary>
    private static int[] SortingLayerIds(string[] names)
    {
        int[] ids = new int[names.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            ids[i] = SortingLayer.NameToID(names[i]);
        }

        return ids;
    }

    /// <summary>
    /// Whether <paramref name="a"/> and <paramref name="b"/> hold the same sorting layer IDs in the same order.
    /// </summary>
    private static bool SameLayers(int[] a, int[] b)
    {
        if (a == null || a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
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
