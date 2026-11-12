using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for where the chat window stands and how large it is (Docs/UiRevamp.md, Phase 12), on the project's HUD
/// settings: its default place under the party frames, moving and resizing kept on the canvas and between the smallest
/// and largest window, the corner a moved window keeps its place by, and a place kept at one UI size holding at another.
/// </summary>
public class ChatWindowLayoutTests
{
    private static readonly Vector2Int Small = new(480, 270);
    private static readonly Vector2Int Normal = new(640, 360);

    private HudSettings settings;
    private ChatWindowLayout layout;

    /// <summary>
    /// The layout of the project's HUD settings.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        settings = UnityEditor.AssetDatabase.LoadAssetAtPath<HudSettings>("Assets/Data/UI/HudSettings.asset");
        layout = ChatWindowLayout.From(settings);
    }

    /// <summary>
    /// The project's window: 144 x 96 UI px, 8 from the left edge and 98 from the top, so 8 under the party frames'
    /// region and, at 480 x 270, 8 over the command bar's; from 104 x 52 to 240 x 160. A place never moved stands
    /// there at every size.
    /// </summary>
    [Test]
    public void TheDefaultWindow_StandsUnderThePartyFrames()
    {
        Assert.That(layout.Default(Small), Is.EqualTo(new RectInt(8, 98, 144, 96)));
        Assert.That(layout.Default(Normal), Is.EqualTo(new RectInt(8, 98, 144, 96)));
        Assert.That(Small.y - layout.Default(Small).yMax, Is.EqualTo(68 + 8), "8 above the command bar's region.");
        Assert.That(layout.MinSize, Is.EqualTo(new Vector2Int(104, 52)));
        Assert.That(layout.MaxSize, Is.EqualTo(new Vector2Int(240, 160)));
        Assert.That(layout.Resolve(default, Small), Is.EqualTo(layout.Default(Small)));
    }

    /// <summary>
    /// Moving keeps the whole window on the canvas; resizing moves the bottom right corner and keeps the size between
    /// the smallest and largest and the corner on the canvas, the top left corner where it was.
    /// </summary>
    [Test]
    public void MovingAndResizing_KeepTheWindowOnTheCanvas_AndBetweenItsSizes()
    {
        RectInt start = layout.Default(Small);
        Assert.That(layout.Moved(start, new Vector2Int(20, -30), Small), Is.EqualTo(new RectInt(28, 68, 144, 96)));
        Assert.That(layout.Moved(start, new Vector2Int(-100, -500), Small), Is.EqualTo(new RectInt(0, 0, 144, 96)));
        Assert.That(layout.Moved(start, new Vector2Int(1000, 1000), Small), Is.EqualTo(new RectInt(480 - 144, 270 - 96, 144, 96)));

        Assert.That(layout.Resized(start, new Vector2Int(30, 10), Small), Is.EqualTo(new RectInt(8, 98, 174, 106)));
        Assert.That(layout.Resized(start, new Vector2Int(500, 500), Small), Is.EqualTo(new RectInt(8, 98, 240, 160)), "No larger than the largest.");
        Assert.That(layout.Resized(start, new Vector2Int(-500, -500), Small), Is.EqualTo(new RectInt(8, 98, 104, 52)), "No smaller than the smallest.");
        RectInt low = new(8, 200, 144, 60);
        Assert.That(layout.Resized(low, new Vector2Int(0, 100), Small).yMax, Is.EqualTo(270), "Its corner stays on the canvas.");
    }

    /// <summary>
    /// A window let go keeps its distance from the canvas corner nearest its centre, and its size; that place gives
    /// the same window back, and at another canvas size it keeps its distance from that corner.
    /// </summary>
    [Test]
    public void AMovedWindow_KeepsItsPlaceByTheNearestCorner_AtEverySize()
    {
        RectInt bottomRight = new(Normal.x - 8 - 200, Normal.y - 80 - 100, 200, 100);
        ChatWindowPlace place = layout.PlaceOf(bottomRight, Normal);
        Assert.That(place.Corner, Is.EqualTo(ScreenCorner.BottomRight));
        Assert.That(place.Offset, Is.EqualTo(new Vector2Int(8, 80)));
        Assert.That(place.Size, Is.EqualTo(new Vector2Int(200, 100)));
        Assert.That(place.IsMoved, Is.True);
        Assert.That(layout.Resolve(place, Normal), Is.EqualTo(bottomRight));
        Assert.That(layout.Resolve(place, Small), Is.EqualTo(new RectInt(Small.x - 8 - 200, Small.y - 80 - 100, 200, 100)));

        Assert.That(layout.PlaceOf(new RectInt(10, 20, 150, 90), Normal).Corner, Is.EqualTo(ScreenCorner.TopLeft));
        Assert.That(layout.PlaceOf(new RectInt(400, 20, 150, 90), Normal).Corner, Is.EqualTo(ScreenCorner.TopRight));
        Assert.That(layout.PlaceOf(new RectInt(10, 250, 150, 90), Normal).Corner, Is.EqualTo(ScreenCorner.BottomLeft));

        ChatWindowPlace far = new(ScreenCorner.TopLeft, new Vector2Int(600, 300), new Vector2Int(400, 400));
        Assert.That(layout.Resolve(far, Small), Is.EqualTo(new RectInt(480 - 240, 270 - 160, 240, 160)), "Kept on a smaller canvas, at the largest size.");
    }
}
