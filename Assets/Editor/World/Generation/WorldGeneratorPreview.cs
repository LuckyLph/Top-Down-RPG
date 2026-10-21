using UnityEngine;

/// <summary>
/// The World Generator window's preview of a composition (Docs/WorldGeneration.md: Behaviour: Generating), one pixel per
/// cell with rows from the bottom, as Phase 1's signed-off preview drew it: each cell in the base colour of its top
/// material, a shade darker for each earlier material of the style's order sharing that colour, the world's weeds and
/// meadow tinted apart from its grass, whose colour they share; raised ground darker and ramps lighter; the chunk borders
/// darkened; the locked chunks hatched; the regions' outlines lighter and their ports magenta (orange while no link
/// uses them); the mobs red (the wilds' pink); the waystones cyan crosses and the gates yellow ones; and each chunk's
/// layer count in a 3 x 5 digit font by its top-left corner, black, red past 16 unless its record allows it wide, and
/// blue past 16 when it does.
/// </summary>
internal static class WorldGeneratorPreview
{
    public static readonly Color32 PortColour = new(255, 0, 255, 255);
    public static readonly Color32 UnlinkedPortColour = new(255, 128, 0, 255);
    public static readonly Color32 MobColour = new(230, 0, 0, 255);
    public static readonly Color32 WildsMobColour = new(255, 60, 160, 255);
    public static readonly Color32 WaystoneColour = new(0, 255, 255, 255);
    public static readonly Color32 GateColour = new(255, 255, 0, 255);
    public static readonly Color32 CountColour = new(0, 0, 0, 255);
    public static readonly Color32 PastNarrowColour = new(255, 0, 0, 255);
    public static readonly Color32 WideColour = new(40, 80, 255, 255);

    private static readonly Color32 WeedsColour = new(96, 128, 30, 255);
    private static readonly Color32 MeadowColour = new(150, 196, 84, 255);
    private static readonly Color32 NoMaterialColour = new(255, 0, 255, 255);
    private static readonly Color32 LockColour = new(20, 24, 48, 255);
    private const float RepeatedColourShade = 0.8f;
    private const float RaisedShade = 0.82f;
    private const float RampLight = 0.4f;
    private const float BorderShade = 0.45f;
    private const float OutlineLight = 0.35f;
    private const float LockShade = 0.55f;
    private const int LockStripeSpacing = 8;
    private const int CountInset = 3;
    private const int DigitAdvance = 4;
    private const int DigitWidth = 3;
    private const int DigitHeight = 5;

    private static readonly string[] Digits =
    {
        "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
        "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111"
    };

    /// <summary>
    /// The preview's pixels for <paramref name="composition"/>, one per cell in row order from the bottom, with the
    /// locks of <paramref name="generator"/>'s chunk records and its weeds and meadow tinted; none for a composition that
    /// failed.
    /// </summary>
    public static Color32[] Pixels(WorldComposition composition, WorldGenerator generator)
    {
        int width = composition.Size.x;
        int height = composition.Size.y;
        Color32[] pixels = new Color32[width * height];
        if (pixels.Length == 0)
        {
            return pixels;
        }

        DrawGround(composition, MaterialColours(composition, generator), pixels);
        if (generator != null)
        {
            DrawLocks(composition, generator, pixels);
        }

        DrawRegions(composition, pixels);
        foreach (WorldComposition.Mob mob in composition.Mobs)
        {
            Put(pixels, width, height, mob.Cell.x, mob.Cell.y, composition.RegionAt(mob.Cell) == WorldComposition.Wilds ? WildsMobColour : MobColour);
        }

        foreach (WorldDefinition.Entry entry in composition.Entries)
        {
            Vector2Int cell = Vector2Int.FloorToInt(entry.Position);
            Color32 colour = entry.Waystone ? WaystoneColour : GateColour;
            for (int d = -1; d <= 1; d++)
            {
                Put(pixels, width, height, cell.x + d, cell.y, colour);
                Put(pixels, width, height, cell.x, cell.y + d, colour);
            }
        }

        foreach (WorldComposition.Chunk chunk in composition.Chunks)
        {
            DrawCount(chunk, pixels, width, height);
        }

        return pixels;
    }

    /// <summary>
    /// Paints <paramref name="composition"/>'s preview into <paramref name="texture"/>, made (point filtered, never
    /// saved) or replaced when missing or of another size, and returns it; destroys it and returns null for a
    /// composition that failed.
    /// </summary>
    public static Texture2D Draw(WorldComposition composition, WorldGenerator generator, Texture2D texture)
    {
        Color32[] pixels = Pixels(composition, generator);
        if (pixels.Length == 0)
        {
            if (texture != null)
            {
                Object.DestroyImmediate(texture);
            }

            return null;
        }

        if (texture == null || texture.width != composition.Size.x || texture.height != composition.Size.y)
        {
            if (texture != null)
            {
                Object.DestroyImmediate(texture);
            }

            texture = new Texture2D(composition.Size.x, composition.Size.y, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    /// <summary>
    /// The colour of each material of <paramref name="composition"/>'s layer order: its base colour, a shade darker for
    /// each earlier material with the same one, and fixed tints for <paramref name="generator"/>'s weeds and meadow.
    /// </summary>
    public static Color32[] MaterialColours(WorldComposition composition, WorldGenerator generator)
    {
        Color32[] colours = new Color32[composition.LayerOrder.Count];
        for (int i = 0; i < colours.Length; i++)
        {
            TerrainMaterial material = composition.LayerOrder[i];
            if (material == null)
            {
                colours[i] = NoMaterialColour;
                continue;
            }

            Color colour = material.BaseColour;
            for (int j = 0; j < i; j++)
            {
                if (composition.LayerOrder[j] != null && composition.LayerOrder[j].BaseColour == material.BaseColour)
                {
                    colour *= RepeatedColourShade;
                }
            }

            colour.a = 1f;
            colours[i] = colour;
            if (generator != null && material == generator.Materials.Weeds)
            {
                colours[i] = WeedsColour;
            }
            else if (generator != null && material == generator.Materials.Meadow)
            {
                colours[i] = MeadowColour;
            }
        }

        return colours;
    }

    /// <summary>
    /// The pixel of the top-left corner of the first digit of the layer count of the chunk at <paramref name="coords"/>.
    /// </summary>
    public static Vector2Int CountOrigin(Vector2Int coords)
    {
        return new Vector2Int(coords.x * ChunkCoords.Size + CountInset, (coords.y + 1) * ChunkCoords.Size - CountInset);
    }

    /// <summary>
    /// Paints every cell in its top material's colour, raised ground darker and ramps lighter, the chunk borders darker.
    /// </summary>
    private static void DrawGround(WorldComposition composition, Color32[] colours, Color32[] pixels)
    {
        int width = composition.Size.x;
        for (int y = 0; y < composition.Size.y; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2Int cell = new(x, y);
                int top = WorldComposition.TopIndex(composition.MaskAt(cell));
                Color colour = top >= 0 ? colours[top] : NoMaterialColour;
                byte raised = composition.RaisedAt(cell);
                if (raised == RaisedGround.Raised)
                {
                    colour *= RaisedShade;
                }
                else if (raised == RaisedGround.Ramp)
                {
                    colour = Color.Lerp(colour, Color.white, RampLight);
                }

                if (x % ChunkCoords.Size == 0 || y % ChunkCoords.Size == 0)
                {
                    colour = Color.Lerp(colour, Color.black, BorderShade);
                }

                colour.a = 1f;
                pixels[y * width + x] = colour;
            }
        }
    }

    /// <summary>
    /// Hatches each chunk that <paramref name="generator"/>'s record locks with dark diagonal stripes.
    /// </summary>
    private static void DrawLocks(WorldComposition composition, WorldGenerator generator, Color32[] pixels)
    {
        int width = composition.Size.x;
        foreach (WorldComposition.Chunk chunk in composition.Chunks)
        {
            ChunkRecord record = generator.RecordOf(chunk.Coords);
            if (record == null || !record.Locked)
            {
                continue;
            }

            RectInt bounds = ChunkCoords.Bounds(chunk.Coords);
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    if ((x - y) % LockStripeSpacing == 0)
                    {
                        int index = y * width + x;
                        pixels[index] = Color32.Lerp(pixels[index], LockColour, LockShade);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Lightens each region's outline and paints its ports.
    /// </summary>
    private static void DrawRegions(WorldComposition composition, Color32[] pixels)
    {
        int width = composition.Size.x;
        int height = composition.Size.y;
        foreach (WorldComposition.Region region in composition.Regions)
        {
            foreach (Vector2Int cell in region.Outline)
            {
                if (composition.Contains(cell))
                {
                    int index = cell.y * width + cell.x;
                    pixels[index] = Color.Lerp(pixels[index], Color.white, OutlineLight);
                }
            }

            foreach (WorldComposition.Port port in region.Ports)
            {
                foreach (Vector2Int cell in port.Cells)
                {
                    Put(pixels, width, height, cell.x, cell.y, port.Linked ? PortColour : UnlinkedPortColour);
                }
            }
        }
    }

    /// <summary>
    /// Writes <paramref name="chunk"/>'s layer count by its top-left corner.
    /// </summary>
    private static void DrawCount(WorldComposition.Chunk chunk, Color32[] pixels, int width, int height)
    {
        int count = chunk.Materials.Count;
        Color32 ink = count <= TerrainControlMap.NarrowLayers ? CountColour : chunk.AllowWide ? WideColour : PastNarrowColour;
        Vector2Int origin = CountOrigin(chunk.Coords);
        string text = count.ToString();
        for (int c = 0; c < text.Length; c++)
        {
            string glyph = Digits[text[c] - '0'];
            for (int row = 0; row < DigitHeight; row++)
            {
                for (int column = 0; column < DigitWidth; column++)
                {
                    if (glyph[row * DigitWidth + column] == '1')
                    {
                        Put(pixels, width, height, origin.x + c * DigitAdvance + column, origin.y - row, ink);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Sets the pixel at (<paramref name="x"/>, <paramref name="y"/>), ignoring pixels outside the preview.
    /// </summary>
    private static void Put(Color32[] pixels, int width, int height, int x, int y, Color32 colour)
    {
        if (x >= 0 && y >= 0 && x < width && y < height)
        {
            pixels[y * width + x] = colour;
        }
    }
}
