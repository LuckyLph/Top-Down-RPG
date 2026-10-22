using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Bakes and writes a <see cref="WeaponSpriteRecipe"/>. The icon is the weapon alone lying in the screen's plane,
/// pointing at the recipe's angle, at its scale or the largest that keeps it inside the icon's margin, centred, written as
/// <c>Assets/Sprites/Weapons/&lt;icon name&gt;.png</c>, one sprite pivoted on its middle. A bow's arrow is drawn at the
/// character's scale in each of the recipe's directions, its tip on each frame's middle, written as one row of frames,
/// <c>&lt;arrow name&gt;.png</c>, sliced into <c>&lt;arrow name&gt;_&lt;direction&gt;</c> pivoted on the tip, and a
/// runtime <see cref="DirectionalSprites"/> <c>&lt;arrow name&gt;_Directions.asset</c> beside it. Baking touches no
/// asset and gives the same pixels for the same recipe; writing skips a file whose bytes did not change, and the set
/// keeps its GUID.
/// </summary>
public static class WeaponSpriteWriter
{
    public const string Folder = "Assets/Sprites/Weapons";
    private const float MinIconScale = 0.25f;
    private const float MaxIconScale = 8f;
    private const float IconScaleStep = 1.02f;

    /// <summary>
    /// Draws the icon: <see cref="WeaponSpriteRecipe.IconSize"/> squared pixels, rows from the bottom, the weapon at
    /// <see cref="IconScaleOf"/> and centred.
    /// </summary>
    public static Color32[] BakeIcon(WeaponSpriteRecipe recipe)
    {
        CharacterBaker baker = new(recipe.Character);
        int size = recipe.IconSize;
        int probe = size * 3;
        Vector2Int probePivot = new(probe / 2, probe / 2);
        float scale = IconScaleOf(recipe);
        RectInt bounds = FilledBounds(DrawIcon(recipe, probe, probePivot, scale));
        Vector2Int pivot = new(
            probePivot.x - bounds.x - bounds.width / 2 + size / 2,
            probePivot.y - bounds.y - bounds.height / 2 + size / 2);
        return baker.Shade(DrawIcon(recipe, size, pivot, scale), recipe.Weapon);
    }

    /// <summary>
    /// The icon's pixels per rig unit: the recipe's, or the largest at which the weapon stays inside the icon's
    /// margin, outline included.
    /// </summary>
    public static float IconScaleOf(WeaponSpriteRecipe recipe)
    {
        if (recipe.IconScale > 0f)
        {
            return recipe.IconScale;
        }

        int size = recipe.IconSize;
        int room = size - 2 * recipe.IconMargin - 2;
        int probe = size * 3;
        Vector2Int probePivot = new(probe / 2, probe / 2);
        float scale = MinIconScale;
        while (scale < MaxIconScale)
        {
            RectInt larger = FilledBounds(DrawIcon(recipe, probe, probePivot, scale * IconScaleStep));
            if (larger.width > room || larger.height > room)
            {
                break;
            }

            scale *= IconScaleStep;
        }

        return scale;
    }

    /// <summary>
    /// Draws the arrow in each direction: a row of <see cref="WeaponSpriteRecipe.ArrowFrame"/>-pixel frames,
    /// counterclockwise from east, rows from the bottom.
    /// </summary>
    public static Color32[] BakeArrow(WeaponSpriteRecipe recipe)
    {
        CharacterBaker baker = new(recipe.Character);
        int frame = recipe.ArrowFrame;
        int count = recipe.ArrowDirections;
        int width = frame * count;
        Color32[] pixels = new Color32[width * frame];
        for (int direction = 0; direction < count; direction++)
        {
            Color32[] colours = baker.Shade(DrawArrow(recipe, direction, baker.Scale), recipe.Weapon);
            for (int y = 0; y < frame; y++)
            {
                System.Array.Copy(colours, y * frame, pixels, y * width + direction * frame, frame);
            }
        }

        return pixels;
    }

    /// <summary>
    /// Writes the icon and, for a bow with an arrow name, the arrow's sheet and direction set; returns the written
    /// paths. Logs an error and writes nothing when the recipe cannot bake.
    /// </summary>
    public static IReadOnlyList<string> Generate(WeaponSpriteRecipe recipe)
    {
        if (!recipe.CanGenerate(out string problem))
        {
            Debug.LogError($"{recipe.name}: {problem}", recipe);
            return null;
        }

        TerrainMaterialWriter.EnsureFolder(Folder);
        List<string> paths = new();
        string iconPath = $"{Folder}/{recipe.IconName}.png";
        int size = recipe.IconSize;
        WriteSheet(iconPath, size, size, BakeIcon(recipe), new[] { new Slice(recipe.IconName, new Rect(0f, 0f, size, size)) });
        paths.Add(iconPath);
        if (!recipe.HasArrow)
        {
            return paths;
        }

        string arrowPath = $"{Folder}/{recipe.ArrowName}.png";
        int frame = recipe.ArrowFrame;
        Slice[] slices = Enumerable.Range(0, recipe.ArrowDirections).Select(d => new Slice(ArrowSliceName(recipe, d), new Rect(d * frame, 0f, frame, frame))).ToArray();
        WriteSheet(arrowPath, frame * recipe.ArrowDirections, frame, BakeArrow(recipe), slices);
        paths.Add(arrowPath);
        paths.Add(WriteDirections(recipe, arrowPath));
        return paths;
    }

    /// <summary>
    /// The name of the arrow's slice for direction <paramref name="direction"/>.
    /// </summary>
    public static string ArrowSliceName(WeaponSpriteRecipe recipe, int direction)
    {
        return $"{recipe.ArrowName}_{direction}";
    }

    /// <summary>
    /// The path of the arrow's direction set.
    /// </summary>
    public static string DirectionsPath(WeaponSpriteRecipe recipe)
    {
        return $"{Folder}/{recipe.ArrowName}_Directions.asset";
    }

    /// <summary>
    /// Where the arrow of direction <paramref name="direction"/> points on the screen, in degrees counterclockwise from
    /// right.
    /// </summary>
    public static float ArrowScreenAngle(WeaponSpriteRecipe recipe, int direction)
    {
        return 360f * direction / recipe.ArrowDirections;
    }

    /// <summary>
    /// Draws the icon onto a new canvas of <paramref name="size"/> squared pixels at <paramref name="scale"/> pixels
    /// per rig unit, lying in the screen's plane: a blade along the angle, its middle near <paramref name="pivot"/>; a
    /// bow's grip on the pivot with its upper limb along the angle, shooting a quarter turn clockwise from it, an arrow
    /// on the string when the recipe has one.
    /// </summary>
    private static CharacterCanvas DrawIcon(WeaponSpriteRecipe recipe, int size, Vector2Int pivot, float scale)
    {
        CharacterCanvas canvas = new(size, size, pivot, scale);
        WeaponPainter painter = new(recipe.Weapon, canvas, ScreenPlane);
        CharacterWeaponArt weapon = recipe.Weapon;
        float radians = recipe.IconAngle * Mathf.Deg2Rad;
        Vector3 along = new(Mathf.Cos(radians), Mathf.Sin(radians), 0f);
        if (weapon.Kind == WeaponKind.Blade)
        {
            painter.DrawBlade(-along * ((weapon.Length - weapon.Pommel) * 0.5f), along, new Vector3(-along.y, along.x, 0f));
            return canvas;
        }

        Vector3 aim = new(along.y, -along.x, 0f);
        Vector3 rest = -aim * weapon.Brace;
        Vector3 drawHand = rest - aim * (weapon.ArrowLength * 0.6f);
        painter.DrawBow(Vector3.zero, along, aim, recipe.IconDraw, 0f, false, drawHand);
        if (recipe.IconArrow)
        {
            painter.DrawArrow(rest + (drawHand - rest) * Mathf.Clamp01(recipe.IconDraw), aim, new Vector3(0f, 0f, 1f));
        }

        return canvas;
    }

    /// <summary>
    /// Draws the arrow of direction <paramref name="direction"/> onto a new frame with its tip on the frame's middle,
    /// at <paramref name="scale"/> pixels per rig unit: level above the ground in the 3/4 view, turned so it points at
    /// the direction's angle on the screen, or lying in the screen's plane.
    /// </summary>
    private static CharacterCanvas DrawArrow(WeaponSpriteRecipe recipe, int direction, float scale)
    {
        int frame = recipe.ArrowFrame;
        CharacterCanvas canvas = new(frame, frame, new Vector2Int(frame / 2, frame / 2), scale);
        float radians = ArrowScreenAngle(recipe, direction) * Mathf.Deg2Rad;
        Vector3 up = new(0f, 0f, 1f);
        if (recipe.ArrowView == ArrowView.ScreenPlane)
        {
            WeaponPainter flat = new(recipe.Weapon, canvas, ScreenPlane);
            Vector3 onScreen = new(Mathf.Cos(radians), Mathf.Sin(radians), 0f);
            flat.DrawArrow(-onScreen * flat.ArrowReach, onScreen, up);
            return canvas;
        }

        float ground = Mathf.Atan2(Mathf.Sin(radians) / Mathf.Sin(CharacterCanvas.ViewPitch * Mathf.Deg2Rad), Mathf.Cos(radians));
        Vector3 along = new(Mathf.Cos(ground), Mathf.Sin(ground), 0f);
        WeaponPainter level = new(recipe.Weapon, canvas, Identity);
        level.DrawArrow(-along * level.ArrowReach, along, up);
        return canvas;
    }

    /// <summary>
    /// A point of a space lying in the screen's plane (x right, y up the screen, z toward the camera) in the world.
    /// </summary>
    private static Vector3 ScreenPlane(Vector3 local)
    {
        return local.x * CharacterCanvas.ViewRight + local.y * CharacterCanvas.ViewUp - local.z * CharacterCanvas.ViewForward;
    }

    /// <summary>
    /// The world itself.
    /// </summary>
    private static Vector3 Identity(Vector3 world)
    {
        return world;
    }

    /// <summary>
    /// The smallest rectangle of pixels holding everything drawn.
    /// </summary>
    private static RectInt FilledBounds(CharacterCanvas canvas)
    {
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                if (!canvas.IsFilled(x, y))
                {
                    continue;
                }

                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
            }
        }

        return minX > maxX ? new RectInt(0, 0, 0, 0) : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>
    /// Writes <paramref name="pixels"/> as a PNG at <paramref name="path"/> when its bytes changed, imports it as a
    /// pixel art sprite sheet and slices it, keeping existing slices' IDs; reimports only when anything changed.
    /// </summary>
    private static void WriteSheet(string path, int width, int height, Color32[] pixels, IReadOnlyList<Slice> slices)
    {
        byte[] png = TerrainMaterialWriter.EncodePng(width, height, pixels);
        bool written = !File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(png) || AssetImporter.GetAtPath(path) == null;
        if (written)
        {
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        bool changed = CharacterSheetWriter.ApplyImportSettings(importer);
        changed |= ApplySlices(importer, slices);
        if (written || changed)
        {
            importer.SaveAndReimport();
        }
    }

    /// <summary>
    /// Gives the sheet exactly <paramref name="slices"/>, each pivoted on its middle, through the sprite editor's data
    /// provider so existing slices keep their IDs, without reimporting. Returns whether anything changed.
    /// </summary>
    private static bool ApplySlices(TextureImporter importer, IReadOnlyList<Slice> slices)
    {
        SpriteDataProviderFactories factories = new();
        factories.Init();
        ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        List<SpriteRect> existing = provider.GetSpriteRects().ToList();
        ISpriteNameFileIdDataProvider fileIds = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        List<SpriteNameFileIdPair> pairs = fileIds.GetNameFileIdPairs().ToList();
        Vector2 middle = new(0.5f, 0.5f);
        List<SpriteRect> rects = new();
        bool changed = false;
        foreach (Slice wanted in slices)
        {
            SpriteRect slice = existing.FirstOrDefault(r => r.name == wanted.Name);
            if (slice == null)
            {
                slice = new SpriteRect { name = wanted.Name, spriteID = GUID.Generate() };
                pairs.Add(new SpriteNameFileIdPair(wanted.Name, slice.spriteID));
                changed = true;
            }

            if (slice.rect != wanted.Rect || slice.alignment != SpriteAlignment.Custom || slice.pivot != middle || slice.border != Vector4.zero)
            {
                slice.rect = wanted.Rect;
                slice.alignment = SpriteAlignment.Custom;
                slice.pivot = middle;
                slice.border = Vector4.zero;
                changed = true;
            }

            rects.Add(slice);
        }

        changed |= rects.Count != existing.Count;
        if (!changed)
        {
            return false;
        }

        HashSet<string> kept = new(rects.Select(r => r.name));
        provider.SetSpriteRects(rects.ToArray());
        fileIds.SetNameFileIdPairs(pairs.Where(p => kept.Contains(p.name)).ToList());
        provider.Apply();
        return true;
    }

    /// <summary>
    /// Writes the arrow's direction set over the sliced sheet at <paramref name="sheetPath"/>: creates it, or copies
    /// the new set over it when it differs, keeping its GUID. Returns its path.
    /// </summary>
    private static string WriteDirections(WeaponSpriteRecipe recipe, string sheetPath)
    {
        Dictionary<string, Sprite> sprites = AssetDatabase.LoadAllAssetsAtPath(sheetPath).OfType<Sprite>().ToDictionary(s => s.name);
        Sprite[] ordered = new Sprite[recipe.ArrowDirections];
        for (int direction = 0; direction < ordered.Length; direction++)
        {
            sprites.TryGetValue(ArrowSliceName(recipe, direction), out ordered[direction]);
        }

        string path = DirectionsPath(recipe);
        string setName = Path.GetFileNameWithoutExtension(path);
        DirectionalSprites fresh = DirectionalSprites.Create(ordered);
        fresh.name = setName;
        DirectionalSprites current = AssetDatabase.LoadAssetAtPath<DirectionalSprites>(path);
        if (current == null)
        {
            AssetDatabase.CreateAsset(fresh, path);
            return path;
        }

        if (EditorJsonUtility.ToJson(current) != EditorJsonUtility.ToJson(fresh))
        {
            EditorUtility.CopySerialized(fresh, current);
            current.name = setName;
            EditorUtility.SetDirty(current);
            AssetDatabase.SaveAssetIfDirty(current);
        }

        Object.DestroyImmediate(fresh);
        return path;
    }

    /// <summary>
    /// One sprite of a sheet: its name and its rectangle in pixels from the bottom left.
    /// </summary>
    private readonly struct Slice
    {
        /// <summary>
        /// Creates a slice named <paramref name="name"/> over <paramref name="rect"/>.
        /// </summary>
        public Slice(string name, Rect rect)
        {
            Name = name;
            Rect = rect;
        }

        public string Name { get; }
        public Rect Rect { get; }
    }
}
