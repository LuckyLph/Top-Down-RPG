using UnityEngine;

/// <summary>
/// One thing drawn in evenly spaced directions, counterclockwise from east, so nothing rotates a sprite at runtime
/// (Docs/Animation.md, Rules): a projectile in flight. Written by the editor's weapon sprite writer beside its sheet;
/// runtime code only reads it.
/// </summary>
public sealed class DirectionalSprites : ScriptableObject
{
    [SerializeField, Tooltip("A sprite per direction, counterclockwise from east, evenly spaced.")]
    private Sprite[] sprites = new Sprite[0];

    public int Count => sprites != null ? sprites.Length : 0;

    /// <summary>
    /// Creates an unsaved set of <paramref name="directionSprites"/>, counterclockwise from east, for the writer and
    /// tests.
    /// </summary>
    public static DirectionalSprites Create(Sprite[] directionSprites)
    {
        DirectionalSprites set = CreateInstance<DirectionalSprites>();
        set.sprites = directionSprites ?? new Sprite[0];
        return set;
    }

    /// <summary>
    /// The index of the direction nearest <paramref name="direction"/>; east for a zero direction, 0 without sprites.
    /// </summary>
    public int IndexOf(Vector2 direction)
    {
        int count = Count;
        if (count == 0 || direction.sqrMagnitude < 1e-8f)
        {
            return 0;
        }

        float turns = Mathf.Atan2(direction.y, direction.x) / (2f * Mathf.PI);
        return ((Mathf.RoundToInt(turns * count) % count) + count) % count;
    }

    /// <summary>
    /// The sprite of direction <paramref name="index"/>, or null when it is out of range.
    /// </summary>
    public Sprite Get(int index)
    {
        return index >= 0 && index < Count ? sprites[index] : null;
    }

    /// <summary>
    /// The sprite of the direction nearest <paramref name="direction"/>, or null without sprites.
    /// </summary>
    public Sprite Facing(Vector2 direction)
    {
        return Get(IndexOf(direction));
    }
}
