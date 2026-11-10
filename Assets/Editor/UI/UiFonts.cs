using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Writes the UI's font assets (Docs/DesignSystem.md, Foundations: Type): one static SDF font asset per Galmuri size in
/// <see cref="Folder"/>, sampled at <see cref="AtlasPixelsPerFontPixel"/> atlas pixels per font pixel with
/// <see cref="Padding"/> pixels of padding, holding <see cref="Characters"/>, and an outlined material preset beside
/// each whose outline is one font pixel wide outside the glyph. Existing assets are rewritten in place, so their
/// references hold.
/// </summary>
public static class UiFonts
{
    public const string Folder = "Assets/Fonts";
    public const int AtlasPixelsPerFontPixel = 6;
    public const int Padding = 12;
    public const float OutlinePixels = 1f;

    /// <summary>The Galmuri sizes the UI uses and the font size, in UI px, at which each draws one UI px per font pixel.</summary>
    public static readonly IReadOnlyList<(string name, int em, int atlasWidth, int atlasHeight)> Fonts = new[]
    {
        ("Galmuri7", 8, 1024, 1024),
        ("Galmuri9", 10, 1024, 1024),
        ("Galmuri11", 12, 2048, 1024),
        ("Galmuri14", 15, 2048, 1024),
    };

    /// <summary>
    /// Every character the static atlases hold: printable ASCII, Latin-1 (key names on other keyboard layouts), dashes,
    /// curly quotes, the bullet, the ellipsis and the zero-width space TMP's input field shows when empty.
    /// </summary>
    public static readonly string Characters = BuildCharacters();

    /// <summary>The path of the font asset written for <paramref name="name"/>.</summary>
    public static string FontAssetPath(string name) => $"{Folder}/{name} SDF.asset";

    /// <summary>The path of the outlined material preset written for <paramref name="name"/>.</summary>
    public static string OutlinePath(string name) => $"{Folder}/{name} SDF Outline.mat";

    /// <summary>
    /// The outline width (and face dilation, which keeps the face whole) that puts <paramref name="pixels"/> font pixels
    /// of outline outside a glyph of a font sampled at <see cref="AtlasPixelsPerFontPixel"/> with <see cref="Padding"/>:
    /// the outline reaches 2 x width x padding atlas pixels outside the glyph, measured on previews (Phase 4 notes).
    /// </summary>
    public static float OutlineWidthFor(float pixels) => pixels * AtlasPixelsPerFontPixel / (2f * Padding);

    /// <summary>
    /// Writes every font asset and outlined material preset, then saves them.
    /// </summary>
    [MenuItem("Tools/TopDownRPG/UI/Write Fonts")]
    public static void Write()
    {
        foreach ((string name, int em, int atlasWidth, int atlasHeight) in Fonts)
        {
            TMP_FontAsset font = WriteFont(name, em, atlasWidth, atlasHeight);
            WriteOutline(name, font);
        }

        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Creates or rewrites the static font asset of <paramref name="name"/> from its TTF in <see cref="Folder"/>.
    /// </summary>
    private static TMP_FontAsset WriteFont(string name, int em, int atlasWidth, int atlasHeight)
    {
        string sourcePath = $"{Folder}/{name}.ttf";
        Font source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
        if (source == null)
        {
            throw new System.InvalidOperationException($"No font at {sourcePath}.");
        }

        int sampling = em * AtlasPixelsPerFontPixel;
        TMP_FontAsset created = TMP_FontAsset.CreateFontAsset(source, sampling, Padding, GlyphRenderMode.SDFAA, atlasWidth, atlasHeight, AtlasPopulationMode.Dynamic, false);
        if (!created.TryAddCharacters(Characters, out string missing))
        {
            throw new System.InvalidOperationException($"{name} does not hold every UI character; missing: {missing}");
        }

        created.atlasPopulationMode = AtlasPopulationMode.Static;
        created.creationSettings = new FontAssetCreationSettings
        {
            sourceFontFileName = source.name,
            sourceFontFileGUID = AssetDatabase.AssetPathToGUID(sourcePath),
            pointSizeSamplingMode = 1,
            pointSize = sampling,
            padding = Padding,
            paddingMode = 2,
            atlasWidth = atlasWidth,
            atlasHeight = atlasHeight,
            characterSetSelectionMode = 7,
            characterSequence = Characters,
            renderMode = (int)GlyphRenderMode.SDFAA,
        };

        string path = FontAssetPath(name);
        TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (existing == null)
        {
            created.name = $"{name} SDF";
            created.atlasTexture.name = $"{name} SDF Atlas";
            created.material.name = $"{name} SDF Material";
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.AddObjectToAsset(created.atlasTexture, created);
            AssetDatabase.AddObjectToAsset(created.material, created);
            return created;
        }

        Texture2D atlas = existing.atlasTexture;
        Material material = existing.material;
        EditorUtility.CopySerialized(created, existing);
        existing.name = $"{name} SDF";
        atlas.Reinitialize(created.atlasTexture.width, created.atlasTexture.height, created.atlasTexture.format, false);
        atlas.LoadRawTextureData(created.atlasTexture.GetRawTextureData());
        atlas.Apply(false, false);
        material.CopyPropertiesFromMaterial(created.material);
        material.SetTexture(ShaderUtilities.ID_MainTex, atlas);
        SerializedObject serialized = new(existing);
        serialized.FindProperty("m_AtlasTextures").GetArrayElementAtIndex(0).objectReferenceValue = atlas;
        serialized.FindProperty("m_Material").objectReferenceValue = material;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(atlas);
        EditorUtility.SetDirty(material);
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(created.atlasTexture);
        Object.DestroyImmediate(created.material);
        Object.DestroyImmediate(created);
        return existing;
    }

    /// <summary>
    /// Creates or rewrites the outlined material preset of <paramref name="font"/>: a black outline of
    /// <see cref="OutlinePixels"/> font pixels outside the glyph, with the face dilated by as much so it stays whole.
    /// </summary>
    private static void WriteOutline(string name, TMP_FontAsset font)
    {
        string path = OutlinePath(name);
        Material outline = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (outline == null)
        {
            outline = new Material(font.material);
            AssetDatabase.CreateAsset(outline, path);
        }
        else
        {
            outline.CopyPropertiesFromMaterial(font.material);
        }

        float width = OutlineWidthFor(OutlinePixels);
        outline.name = $"{name} SDF Outline";
        outline.EnableKeyword(ShaderUtilities.Keyword_Outline);
        outline.SetTexture(ShaderUtilities.ID_MainTex, font.atlasTexture);
        outline.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
        outline.SetFloat(ShaderUtilities.ID_FaceDilate, width);
        outline.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
        ShaderUtilities.UpdateShaderRatios(outline);
        EditorUtility.SetDirty(outline);
    }

    /// <summary>
    /// Builds <see cref="Characters"/>.
    /// </summary>
    private static string BuildCharacters()
    {
        StringBuilder characters = new();
        for (char c = ' '; c <= '~'; c++)
        {
            characters.Append(c);
        }

        for (char c = ' '; c <= 'ÿ'; c++)
        {
            characters.Append(c);
        }

        characters.Append("–—‘’“”•…​");
        return characters.ToString();
    }
}
