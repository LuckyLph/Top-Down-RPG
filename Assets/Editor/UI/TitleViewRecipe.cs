using UnityEngine;

/// <summary>
/// An editor-only recipe for what stands behind the main menu (Docs/UiRevamp.md, Phase 11): the title view, a still
/// picture of a world at a time of day, and the logo, the game's name drawn by a recipe until it has one drawn by hand.
/// <list type="bullet">
/// <item><b>The view</b> is <see cref="Size"/> art px of <see cref="World"/> centred on <see cref="Centre"/>, rendered at
/// <see cref="Phase"/> of the day with every prop and light the world's scenes hold, the terrain's animation stopped at
/// <see cref="TerrainTime"/> so a render is repeatable; <see cref="TitleViewWriter"/> renders it. It is wide and tall
/// enough to cover every canvas in the Scale table, centred.</item>
/// <item><b>The logo</b> is <see cref="Title"/> in <see cref="Font"/> at <see cref="FontSize"/> px, where each of the
/// font's pixels is one, every pixel made <see cref="Scale"/> x <see cref="Scale"/>, in white for an image to tint by its
/// colour role, outlined by <see cref="Outline"/> px in black. <see cref="GenerateLogo"/> draws it without touching an
/// asset, the same settings always giving the same pixels.</item>
/// </list>
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/UI/Title View Recipe", fileName = "Recipe_TitleView")]
public sealed class TitleViewRecipe : ScriptableObject
{
    public const string Folder = "Assets/Sprites/UI/Title";
    public const string ViewPath = Folder + "/TitleView.png";
    public const string LogoPath = Folder + "/Logo.png";
    public const int ArtPixelsPerUnit = 32;
    private const float Coverage = 0.5f;

    [Header("View")]
    [SerializeField, Tooltip("The world the view shows.")] private WorldDefinition world;
    [SerializeField, Tooltip("The view's centre in the world's own cells, rounded to whole art px.")] private Vector2 centre = new(79f, 131f);
    [SerializeField, Tooltip("The view's size in art px, one UI px each; it must cover every canvas in the Scale table.")] private Vector2Int size = new(768, 432);
    [SerializeField, Range(0f, 1f), Tooltip("The phase of the day it is lit at (Assets/Data/Lighting/DayNight.asset): 0.79 is the sunset's last minutes, the sun low and the lights coming on.")]
    private float phase = 0.79f;
    [SerializeField, Min(0f), Tooltip("The time, in seconds, the terrain's animated materials (water, swaying weeds) are drawn at.")] private float terrainTime;

    [Header("Logo")]
    [SerializeField] private string title = "Top-Down RPG";
    [SerializeField, Tooltip("The pixel font the name is drawn in.")] private Font font;
    [SerializeField, Min(1), Tooltip("The font's size in px at which each of its pixels is one.")] private int fontSize = 12;
    [SerializeField, Range(1, 4), Tooltip("How many px each of the font's pixels becomes, across and down.")] private int scale = 2;
    [SerializeField, Range(0, 2), Tooltip("How many px of black outline the name gets.")] private int outline = 1;

    public WorldDefinition World => world;
    public Vector2 Centre => centre;
    public Vector2Int Size => size;
    public float Phase => phase;
    public float TerrainTime => terrainTime;
    public string Title => title;
    public Font Font => font;
    public int FontSize => fontSize;
    public int Scale => scale;
    public int Outline => outline;

    /// <summary>
    /// The view's rectangle in world units: <see cref="Size"/> art px around <see cref="Centre"/>, its corners on whole
    /// art px.
    /// </summary>
    public Rect ViewRect()
    {
        Vector2 middle = world.ToWorld(centre);
        Vector2 half = new(size.x * 0.5f / ArtPixelsPerUnit, size.y * 0.5f / ArtPixelsPerUnit);
        Vector2 min = middle - half;
        min = new Vector2(Mathf.Round(min.x * ArtPixelsPerUnit) / ArtPixelsPerUnit, Mathf.Round(min.y * ArtPixelsPerUnit) / ArtPixelsPerUnit);
        return new Rect(min, half * 2f);
    }

    /// <summary>
    /// Whether the settings can draw both pictures; <paramref name="problem"/> says why not.
    /// </summary>
    public bool CanGenerate(out string problem)
    {
        problem = null;
        if (world == null)
        {
            problem = "The recipe names no world.";
        }
        else if (world.RootScene == null || !world.RootScene.IsValid)
        {
            problem = $"The world {world.name} has no root scene.";
        }
        else if (size.x <= 0 || size.y <= 0)
        {
            problem = "The view has no size.";
        }
        else if (!world.LocalBounds.Contains(Vector2Int.FloorToInt(centre)))
        {
            problem = $"The view's centre {centre} lies outside the world {world.name}.";
        }
        else if (font == null)
        {
            problem = "The recipe names no font for the logo.";
        }
        else if (string.IsNullOrEmpty(title))
        {
            problem = "The logo has no title.";
        }

        return problem == null;
    }

    /// <summary>
    /// Draws the logo: the title's glyphs at <see cref="FontSize"/>, a font pixel drawn where the font covers at least
    /// half of it, each made <see cref="Scale"/> px square, cropped to them and outlined by <see cref="Outline"/> px all
    /// round (corners included). Rows run from the bottom up, as a texture's do.
    /// </summary>
    public LogoPicture GenerateLogo()
    {
        bool[,] glyphs = RasterizeTitle(out int width, out int height);
        int pad = outline;
        int w = width * scale + pad * 2;
        int h = height * scale + pad * 2;
        Color32[] pixels = new Color32[w * h];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!glyphs[x, y])
                {
                    continue;
                }

                for (int dy = -pad; dy < scale + pad; dy++)
                {
                    for (int dx = -pad; dx < scale + pad; dx++)
                    {
                        int px = pad + x * scale + dx;
                        int py = pad + y * scale + dy;
                        int index = py * w + px;
                        bool inside = dx >= 0 && dx < scale && dy >= 0 && dy < scale;
                        if (inside)
                        {
                            pixels[index] = new Color32(255, 255, 255, 255);
                        }
                        else if (pixels[index].a == 0)
                        {
                            pixels[index] = new Color32(0, 0, 0, 255);
                        }
                    }
                }
            }
        }

        return new LogoPicture(w, h, pixels);
    }

    /// <summary>
    /// The title's glyphs as font pixels, cropped to the ones drawn, rows from the bottom up.
    /// </summary>
    private bool[,] RasterizeTitle(out int width, out int height)
    {
        font.RequestCharactersInTexture(title, fontSize, FontStyle.Normal);
        Texture atlas = font.material.mainTexture;
        Color32[] texels = ReadTexture(atlas);
        int cursor = 0;
        int bottom = int.MaxValue;
        int top = int.MinValue;
        foreach (char character in title)
        {
            font.GetCharacterInfo(character, out CharacterInfo info, fontSize, FontStyle.Normal);
            bottom = Mathf.Min(bottom, info.minY);
            top = Mathf.Max(top, info.maxY);
            cursor += info.advance;
        }

        int fullWidth = cursor + fontSize;
        int fullHeight = top - bottom;
        bool[,] drawn = new bool[fullWidth, fullHeight];
        int minX = int.MaxValue;
        int maxX = int.MinValue;
        int minY = int.MaxValue;
        int maxY = int.MinValue;
        cursor = 0;
        foreach (char character in title)
        {
            font.GetCharacterInfo(character, out CharacterInfo info, fontSize, FontStyle.Normal);
            for (int y = 0; y < info.glyphHeight; y++)
            {
                for (int x = 0; x < info.glyphWidth; x++)
                {
                    float fx = (x + 0.5f) / info.glyphWidth;
                    float fy = (y + 0.5f) / info.glyphHeight;
                    Vector2 uv = Vector2.Lerp(Vector2.Lerp(info.uvBottomLeft, info.uvBottomRight, fx), Vector2.Lerp(info.uvTopLeft, info.uvTopRight, fx), fy);
                    int tx = Mathf.Clamp(Mathf.FloorToInt(uv.x * atlas.width), 0, atlas.width - 1);
                    int ty = Mathf.Clamp(Mathf.FloorToInt(uv.y * atlas.height), 0, atlas.height - 1);
                    Color32 texel = texels[ty * atlas.width + tx];
                    int px = cursor + info.minX + x;
                    int py = info.minY + y - bottom;
                    if (Mathf.Max(texel.a, texel.r) < Coverage * 255f || px < 0 || px >= fullWidth || py < 0 || py >= fullHeight)
                    {
                        continue;
                    }

                    drawn[px, py] = true;
                    minX = Mathf.Min(minX, px);
                    maxX = Mathf.Max(maxX, px);
                    minY = Mathf.Min(minY, py);
                    maxY = Mathf.Max(maxY, py);
                }
            }

            cursor += info.advance;
        }

        width = maxX - minX + 1;
        height = maxY - minY + 1;
        bool[,] cropped = new bool[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                cropped[x, y] = drawn[minX + x, minY + y];
            }
        }

        return cropped;
    }

    /// <summary>
    /// The texels of <paramref name="texture"/>, read back through a render texture since a font's atlas is not readable.
    /// </summary>
    private static Color32[] ReadTexture(Texture texture)
    {
        RenderTexture target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        RenderTexture previous = RenderTexture.active;
        Texture2D read = new(texture.width, texture.height, TextureFormat.RGBA32, false, true);
        try
        {
            Graphics.Blit(texture, target);
            RenderTexture.active = target;
            read.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            read.Apply();
            return read.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            DestroyImmediate(read);
        }
    }
}
