using UnityEngine;

/// <summary>
/// How an arrow in flight is turned toward the screen for each of its directions: in the game's 3/4 view (as it flies
/// level above the ground, so it looks shorter going north or south, as the characters' strides do), or lying in the
/// screen's plane (the same length in every direction).
/// </summary>
public enum ArrowView
{
    ThreeQuarter,
    ScreenPlane
}

/// <summary>
/// An editor-only recipe for the sprites a weapon needs beside the character's frames (Docs/Animation.md, Phase 4):
/// its HUD icon, the weapon drawn alone lying in the screen's plane at an angle, and for a bow the arrow it shoots,
/// baked in evenly spaced directions so nothing rotates it at runtime. Drawn by the character baker's rasteriser
/// (<see cref="WeaponPainter"/>) with the character recipe's shading, the arrow at the character's scale so it matches
/// the one in the bow's frames. <see cref="WeaponSpriteWriter"/> bakes it without touching an asset and writes it under
/// <c>Assets/Sprites/Weapons/</c>.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Characters/Weapon Sprite Recipe", fileName = "WeaponSprites")]
public sealed class WeaponSpriteRecipe : ScriptableObject
{
    [Tooltip("The weapon drawn.")]
    [SerializeField] private CharacterWeaponArt weapon;
    [Tooltip("The character whose shading the sprites use, and whose scale the arrow is drawn at.")]
    [SerializeField] private CharacterRecipe character;

    [Header("Icon")]
    [Tooltip("The icon's file and sprite name under Assets/Sprites/Weapons. Letters, digits and underscores only.")]
    [SerializeField] private string iconName = "Icon_Weapon";
    [Tooltip("The icon's width and height in pixels.")]
    [SerializeField, Range(16, 128)] private int iconSize = 32;
    [Tooltip("Where the weapon points on the screen, in degrees counterclockwise from right: a blade's tip, a bow's upper limb.")]
    [SerializeField, Range(0f, 360f)] private float iconAngle = 45f;
    [Tooltip("How far the drawing keeps from the icon's edge, in pixels, outline included.")]
    [SerializeField, Range(0, 8)] private int iconMargin = 1;
    [Tooltip("Pixels per rig unit, so weapons' icons keep their sizes against each other; 0 draws the weapon as large as the icon holds.")]
    [SerializeField, Min(0f)] private float iconScale;
    [Tooltip("A bow's draw in the icon, 0 at rest to 1 fully drawn.")]
    [SerializeField, Range(0f, 1f)] private float iconDraw;
    [Tooltip("A bow's icon shows an arrow on the string.")]
    [SerializeField] private bool iconArrow = true;

    [Header("Arrow (bows)")]
    [Tooltip("The arrow sheet's file name under Assets/Sprites/Weapons, its slices' prefix and its direction set's name. Empty for none.")]
    [SerializeField] private string arrowName = string.Empty;
    [Tooltip("How many directions the arrow is drawn in, evenly spaced counterclockwise from east.")]
    [SerializeField, Range(8, 64)] private int arrowDirections = 32;
    [Tooltip("Each direction's frame, in pixels; the arrow's tip is its middle, the sprite's pivot.")]
    [SerializeField, Range(16, 96)] private int arrowFrame = 56;
    [SerializeField] private ArrowView arrowView = ArrowView.ThreeQuarter;

    public CharacterWeaponArt Weapon => weapon;
    public CharacterRecipe Character => character;
    public string IconName => iconName;
    public int IconSize => iconSize;
    public float IconAngle => iconAngle;
    public int IconMargin => iconMargin;
    public float IconScale => iconScale;
    public float IconDraw => iconDraw;
    public bool IconArrow => iconArrow;
    public string ArrowName => arrowName;
    public bool HasArrow => weapon != null && weapon.Kind == WeaponKind.Bow && !string.IsNullOrEmpty(arrowName);
    public int ArrowDirections => arrowDirections;
    public int ArrowFrame => arrowFrame;
    public ArrowView ArrowView => arrowView;

    /// <summary>
    /// Creates an unsaved recipe, for tests and previews.
    /// </summary>
    public static WeaponSpriteRecipe Create(CharacterWeaponArt weaponArt, CharacterRecipe characterRecipe, string icon, int size, float angle, string arrow = "", int directions = 32, int frame = 56, ArrowView view = ArrowView.ThreeQuarter, float draw = 0f, bool nocked = true, float scale = 0f, int margin = 1)
    {
        WeaponSpriteRecipe recipe = CreateInstance<WeaponSpriteRecipe>();
        recipe.weapon = weaponArt;
        recipe.character = characterRecipe;
        recipe.iconName = icon;
        recipe.iconSize = size;
        recipe.iconAngle = angle;
        recipe.iconScale = scale;
        recipe.iconMargin = margin;
        recipe.iconDraw = draw;
        recipe.iconArrow = nocked;
        recipe.arrowName = arrow;
        recipe.arrowDirections = directions;
        recipe.arrowFrame = frame;
        recipe.arrowView = view;
        recipe.hideFlags = HideFlags.HideAndDontSave;
        return recipe;
    }

    /// <summary>
    /// Whether the recipe can bake: a weapon, a character that can bake, and names of letters, digits and
    /// underscores. Otherwise <paramref name="problem"/> says what is missing.
    /// </summary>
    public bool CanGenerate(out string problem)
    {
        if (weapon == null || character == null)
        {
            problem = "Pick the weapon and the character.";
            return false;
        }

        if (!character.CanGenerate(out problem))
        {
            return false;
        }

        if (!IsName(iconName) || (!string.IsNullOrEmpty(arrowName) && !IsName(arrowName)))
        {
            problem = "Names may only hold letters, digits and underscores.";
            return false;
        }

        problem = null;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="value"/> is a non-empty name of letters, digits and underscores.
    /// </summary>
    private static bool IsName(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (char c in value)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }
}
