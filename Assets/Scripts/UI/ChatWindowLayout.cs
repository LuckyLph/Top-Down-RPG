using UnityEngine;

/// <summary>
/// Where the chat window stands and how large it is (Docs/UiRevamp.md, Phase 12), in whole UI px on a canvas, measured
/// from its top left corner with y going down. A window never moved stands at its default place under the party frames.
/// One the player moved keeps its distance from the canvas corner nearest it when it was let go
/// (<see cref="ChatWindowPlace"/>), so it holds its place at every UI size. Its size stays between the smallest and
/// largest the HUD's settings allow and never passes the canvas, and the whole window stays on the canvas.
/// </summary>
public sealed class ChatWindowLayout
{
    private readonly Vector2Int defaultOffset;
    private readonly Vector2Int defaultSize;
    private readonly Vector2Int minSize;
    private readonly Vector2Int maxSize;

    /// <summary>
    /// A layout whose default window stands <paramref name="offset"/> UI px from the canvas's left and top edges,
    /// <paramref name="size"/> large, and whose windows are <paramref name="min"/> to <paramref name="max"/> large.
    /// </summary>
    public ChatWindowLayout(Vector2Int offset, Vector2Int size, Vector2Int min, Vector2Int max)
    {
        defaultOffset = offset;
        minSize = Vector2Int.Max(min, Vector2Int.one);
        maxSize = Vector2Int.Max(max, minSize);
        defaultSize = Vector2Int.Min(Vector2Int.Max(size, minSize), maxSize);
    }

    /// <summary>
    /// The layout of <paramref name="settings"/>' chat window.
    /// </summary>
    public static ChatWindowLayout From(HudSettings settings)
    {
        return new ChatWindowLayout(settings.ChatOffset, settings.ChatSize, settings.ChatMinSize, settings.ChatMaxSize);
    }

    public Vector2Int MinSize => minSize;
    public Vector2Int MaxSize => maxSize;

    /// <summary>
    /// The default window on a canvas of <paramref name="canvas"/> UI px.
    /// </summary>
    public RectInt Default(Vector2Int canvas)
    {
        return Fit(new RectInt(defaultOffset, defaultSize), canvas);
    }

    /// <summary>
    /// The window <paramref name="place"/> describes on a canvas of <paramref name="canvas"/> UI px: the default one
    /// for a place never moved.
    /// </summary>
    public RectInt Resolve(ChatWindowPlace place, Vector2Int canvas)
    {
        if (!place.IsMoved)
        {
            return Default(canvas);
        }

        Vector2Int size = place.Size;
        Vector2Int offset = place.Offset;
        bool right = place.Corner == ScreenCorner.TopRight || place.Corner == ScreenCorner.BottomRight;
        bool bottom = place.Corner == ScreenCorner.BottomLeft || place.Corner == ScreenCorner.BottomRight;
        Vector2Int sized = ClampSize(size, canvas);
        int x = right ? canvas.x - offset.x - sized.x : offset.x;
        int y = bottom ? canvas.y - offset.y - sized.y : offset.y;
        return Fit(new RectInt(x, y, sized.x, sized.y), canvas);
    }

    /// <summary>
    /// <paramref name="start"/> moved by <paramref name="delta"/> UI px, kept on a canvas of <paramref name="canvas"/>.
    /// </summary>
    public RectInt Moved(RectInt start, Vector2Int delta, Vector2Int canvas)
    {
        return Fit(new RectInt(start.position + delta, start.size), canvas);
    }

    /// <summary>
    /// <paramref name="start"/> with its bottom right corner moved by <paramref name="delta"/> UI px: its size kept
    /// between the smallest and largest and its corner on a canvas of <paramref name="canvas"/>, its top left corner
    /// where it was.
    /// </summary>
    public RectInt Resized(RectInt start, Vector2Int delta, Vector2Int canvas)
    {
        Vector2Int size = Vector2Int.Max(Vector2Int.Min(start.size + delta, maxSize), minSize);
        size = Vector2Int.Min(size, Vector2Int.Max(canvas - start.position, minSize));
        return Fit(new RectInt(start.position, size), canvas);
    }

    /// <summary>
    /// The place to keep for <paramref name="window"/> on a canvas of <paramref name="canvas"/>: its distance from the
    /// canvas corner nearest its centre, and its size.
    /// </summary>
    public ChatWindowPlace PlaceOf(RectInt window, Vector2Int canvas)
    {
        bool right = window.center.x > canvas.x * 0.5f;
        bool bottom = window.center.y > canvas.y * 0.5f;
        ScreenCorner corner = bottom ? (right ? ScreenCorner.BottomRight : ScreenCorner.BottomLeft) : (right ? ScreenCorner.TopRight : ScreenCorner.TopLeft);
        Vector2Int offset = new(right ? canvas.x - window.xMax : window.x, bottom ? canvas.y - window.yMax : window.y);
        return new ChatWindowPlace(corner, offset, window.size);
    }

    /// <summary>
    /// <paramref name="window"/> with its size kept between the smallest and largest and within the canvas, moved onto
    /// a canvas of <paramref name="canvas"/> UI px.
    /// </summary>
    public RectInt Fit(RectInt window, Vector2Int canvas)
    {
        Vector2Int size = ClampSize(window.size, canvas);
        int x = Mathf.Clamp(window.x, 0, Mathf.Max(0, canvas.x - size.x));
        int y = Mathf.Clamp(window.y, 0, Mathf.Max(0, canvas.y - size.y));
        return new RectInt(x, y, size.x, size.y);
    }

    /// <summary>
    /// <paramref name="size"/> kept between the smallest and largest, and no larger than the canvas.
    /// </summary>
    private Vector2Int ClampSize(Vector2Int size, Vector2Int canvas)
    {
        Vector2Int clamped = Vector2Int.Max(Vector2Int.Min(size, maxSize), minSize);
        return Vector2Int.Max(Vector2Int.Min(clamped, canvas), Vector2Int.one);
    }
}
