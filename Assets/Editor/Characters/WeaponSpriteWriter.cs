using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Bakes and writes a <see cref="WeaponSpriteRecipe"/>. The icon is the weapon alone lying in the screen's plane,
/// pointing at the recipe's angle, at its scale or the largest that keeps it inside the icon's margin, centred, written as
/// <c>Assets/Sprites/Weapons/&lt;icon name&gt;.png</c>, one sprite pivoted on its middle. A bow's arrow is drawn at the
/// character's scale in each of the recipe's directions, its tip on each frame's middle, written as one row of frames,
/// <c>&lt;arrow name&gt;.png</c>, sliced into <c>&lt;arrow name&gt;_&lt;direction&gt;</c> pivoted on the tip, and a
/// runtime <see cref="DirectionalSprites"/> <c>&lt;arrow name&gt;_Directions.asset</c> beside it. Baking touches no
/// asset and gives the same pixels for the same recipe; writing goes through <see cref="SpriteSheetFiles"/> (the arrow's
/// through <see cref="WriteDirections"/>, shared with the projectiles drawn in Aseprite), skips a file whose bytes did
/// not change, keeps slice IDs by name, and the set keeps its GUID.
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
        string iconPath = SheetPath(recipe.IconName);
        int size = recipe.IconSize;
        SpriteSheetFiles.WriteSheet(iconPath, size, size, BakeIcon(recipe), new[] { new SpriteSheetFiles.Slice(recipe.IconName, new Rect(0f, 0f, size, size)) });
        paths.Add(iconPath);
        if (!recipe.HasArrow)
        {
            return paths;
        }

        int frame = recipe.ArrowFrame;
        WriteDirections(recipe.ArrowName, new Vector2Int(frame, frame), new Vector2(0.5f, 0.5f), recipe.ArrowDirections, BakeArrow(recipe));
        paths.Add(SheetPath(recipe.ArrowName));
        paths.Add(DirectionsPath(recipe));
        return paths;
    }

    /// <summary>
    /// Writes a thing drawn in <paramref name="count"/> directions, one frame of <paramref name="frameSize"/> each
    /// counterclockwise from east: <paramref name="pixels"/> (rows from the bottom, one row of frames) at
    /// <see cref="SheetPath"/> through <see cref="SpriteSheetFiles.WriteSheet"/>, sliced into
    /// <see cref="DirectionSliceName"/> pivoted at <paramref name="pivot"/> (a share of the frame from its bottom left:
    /// the arrow's tip), then its <see cref="DirectionalSprites"/> at <see cref="DirectionsPath(string)"/>, keeping its
    /// GUID. Shared by the bow's baked arrow and the projectiles drawn in Aseprite
    /// (<see cref="AsepriteProjectileWriter"/>). Returns the paths it wrote: the sheet when it was written or its import
    /// settings or slices changed, the set when it was created or changed.
    /// </summary>
    public static IReadOnlyList<string> WriteDirections(string sheetName, Vector2Int frameSize, Vector2 pivot, int count, Color32[] pixels)
    {
        TerrainMaterialWriter.EnsureFolder(Folder);
        string sheetPath = SheetPath(sheetName);
        SpriteSheetFiles.Slice[] slices = Enumerable.Range(0, count).Select(d => new SpriteSheetFiles.Slice(DirectionSliceName(sheetName, d), new Rect(d * frameSize.x, 0f, frameSize.x, frameSize.y), pivot)).ToArray();
        List<string> written = new();
        if (SpriteSheetFiles.WriteSheet(sheetPath, frameSize.x * count, frameSize.y, pixels, slices))
        {
            written.Add(sheetPath);
        }

        Dictionary<string, Sprite> sprites = SpriteSheetFiles.SpritesByName(sheetPath);
        Sprite[] ordered = slices.Select(s => sprites.TryGetValue(s.Name, out Sprite sprite) ? sprite : null).ToArray();
        string directionsPath = DirectionsPath(sheetName);
        if (SpriteSheetFiles.WriteAsset(directionsPath, DirectionalSprites.Create(ordered)))
        {
            written.Add(directionsPath);
        }

        return written;
    }

    /// <summary>
    /// The path of the sheet named <paramref name="sheetName"/>: an icon, or the frames of a thing drawn in directions.
    /// </summary>
    public static string SheetPath(string sheetName)
    {
        return $"{Folder}/{sheetName}.png";
    }

    /// <summary>
    /// The name of the slice for direction <paramref name="direction"/> of the sheet named <paramref name="sheetName"/>.
    /// </summary>
    public static string DirectionSliceName(string sheetName, int direction)
    {
        return $"{sheetName}_{direction}";
    }

    /// <summary>
    /// The path of the arrow's direction set.
    /// </summary>
    public static string DirectionsPath(WeaponSpriteRecipe recipe)
    {
        return DirectionsPath(recipe.ArrowName);
    }

    /// <summary>
    /// The path of the direction set of the sheet named <paramref name="sheetName"/>.
    /// </summary>
    public static string DirectionsPath(string sheetName)
    {
        return $"{Folder}/{sheetName}_Directions.asset";
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
}
