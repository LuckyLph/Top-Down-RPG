using UnityEngine;

/// <summary>
/// Draws the character on the sprite renderer beside it in a player's colours (Docs/PlayerCharacter.md, Phase 5): the
/// palette of its <see cref="CharacterColourPicks"/> in a small texture of its own, which the lit sprite shader reads
/// wherever a sheet's palette map names a dye (<see cref="CharacterPaletteMap"/>), on a copy of the renderer's material
/// made for it, so the renderer stays in the SRP Batcher and the hit flash's property block leaves it alone. Picks that
/// show the look's own colours put the renderer's own material back. A look whose sheets carry no palette map (one
/// drawn in Aseprite) shows its own colours either way. The copy and the palette are this component's own and are
/// destroyed with it. Visual only, on every machine.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class CharacterPalette : MonoBehaviour
{
    private static readonly int PaletteId = Shader.PropertyToID(CharacterPaletteMap.PaletteTextureName);

    private readonly Color32[] pixels = new Color32[CharacterPaletteMap.PaletteSize];
    private SpriteRenderer body;
    private Material own;
    private Material copy;
    private Texture2D palette;

    /// <summary>
    /// Whether the renderer draws in a palette rather than its own material.
    /// </summary>
    public bool IsShown => body != null && copy != null && body.sharedMaterial == copy;

    /// <summary>
    /// The palette last shown, a row of tones per dye; null before any.
    /// </summary>
    internal Texture2D Palette => palette;

    /// <summary>
    /// The renderer's material the palette's copy was made from.
    /// </summary>
    internal Material Own => own;

    /// <summary>
    /// The copy of the renderer's material drawing in the palette; null before any palette shows.
    /// </summary>
    internal Material Copy => copy;

    /// <summary>
    /// Caches the renderer and its own material.
    /// </summary>
    private void Awake()
    {
        Resolve();
    }

    /// <summary>
    /// Puts the renderer's own material back and destroys the copy and the palette.
    /// </summary>
    private void OnDestroy()
    {
        Clear();
        if (copy != null)
        {
            Destroy(copy);
        }

        if (palette != null)
        {
            Destroy(palette);
        }
    }

    /// <summary>
    /// Draws the character in the colours <paramref name="picks"/> picks of <paramref name="colours"/>; picks of the
    /// look's own colours, or no colours, put the renderer's own material back.
    /// </summary>
    public void Show(CharacterColours colours, CharacterColourPicks picks)
    {
        Resolve();
        if (colours == null || picks.IsDefault || own == null)
        {
            Clear();
            return;
        }

        colours.WritePalette(picks, pixels);
        if (palette == null)
        {
            palette = new Texture2D(ColourRamp.ToneCount, CharacterPaletteMap.Rows, TextureFormat.RGBA32, false, false)
            {
                name = $"{name} Palette",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        palette.SetPixels32(pixels);
        palette.Apply(false, false);
        if (copy == null)
        {
            copy = new Material(own) { name = $"{own.name} ({name} Palette)" };
            copy.SetTexture(PaletteId, palette);
        }

        body.sharedMaterial = copy;
    }

    /// <summary>
    /// Draws the character in the look's own colours: the renderer's own material.
    /// </summary>
    public void Clear()
    {
        if (body != null && own != null && body.sharedMaterial != own)
        {
            body.sharedMaterial = own;
        }
    }

    /// <summary>
    /// Finds the renderer and keeps its own material, once.
    /// </summary>
    private void Resolve()
    {
        if (body == null)
        {
            body = GetComponent<SpriteRenderer>();
        }

        if (own == null && body != null && body.sharedMaterial != copy)
        {
            own = body.sharedMaterial;
        }
    }
}
