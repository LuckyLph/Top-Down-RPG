using System;
using UnityEngine;

/// <summary>
/// Where the player moved the chat window and how large they made it (Docs/UiRevamp.md, Phase 12), as written to the
/// settings file: the canvas corner it keeps its place by, its distance in UI px from that corner's two edges, and its
/// size in UI px. The default value, with no corner, means the window was never moved: it stands at the HUD's default
/// place under the party frames.
/// </summary>
[Serializable]
public struct ChatWindowPlace : IEquatable<ChatWindowPlace>
{
    [SerializeField] private int corner;
    [SerializeField] private int x;
    [SerializeField] private int y;
    [SerializeField] private int width;
    [SerializeField] private int height;

    /// <summary>
    /// A window <paramref name="offset"/> UI px from the edges of the canvas corner <paramref name="screenCorner"/>,
    /// <paramref name="size"/> UI px large.
    /// </summary>
    public ChatWindowPlace(ScreenCorner screenCorner, Vector2Int offset, Vector2Int size)
    {
        corner = (int)screenCorner;
        x = offset.x;
        y = offset.y;
        width = size.x;
        height = size.y;
    }

    /// <summary>
    /// The corner the window keeps its place by; <see cref="ScreenCorner.None"/> for a window never moved, or a value
    /// this build does not know.
    /// </summary>
    public ScreenCorner Corner => corner > (int)ScreenCorner.None && corner <= (int)ScreenCorner.BottomRight ? (ScreenCorner)corner : ScreenCorner.None;

    /// <summary>
    /// Whether the player moved or resized the window: it has a corner and a size.
    /// </summary>
    public bool IsMoved => Corner != ScreenCorner.None && width > 0 && height > 0;

    /// <summary>
    /// The window's distance in UI px from its corner's vertical edge (x) and horizontal edge (y).
    /// </summary>
    public Vector2Int Offset => new(x, y);

    /// <summary>
    /// The window's size in UI px.
    /// </summary>
    public Vector2Int Size => new(width, height);

    /// <summary>
    /// Whether both places are the same.
    /// </summary>
    public bool Equals(ChatWindowPlace other)
    {
        return corner == other.corner && x == other.x && y == other.y && width == other.width && height == other.height;
    }

    /// <summary>
    /// Whether <paramref name="obj"/> is the same place.
    /// </summary>
    public override bool Equals(object obj)
    {
        return obj is ChatWindowPlace other && Equals(other);
    }

    /// <summary>
    /// A hash of every field.
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(corner, x, y, width, height);
    }
}
