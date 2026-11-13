using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for the HUD's chat (Docs/UiRevamp.md, Phase 12) on a copy of <c>PlayerHudCanvas</c> laid out at 640 x 360 UI
/// px, with fakes for the chat key, the session and the sender, a manual clock, and the real input gate and menu stack:
/// it shows online only; the Chat key opens the window, turning gameplay input off and taking Escape, which closes it;
/// Enter sends the cleaned line and closes it, an empty line just closes it, and the key's own press neither submits
/// nor reopens it; lines show newest at the bottom in their slot's colour, the party's muted, with timestamps when
/// asked, escaped, as many whole ones as fit; a line shows for its time, fades, and shows again while open; the window
/// stands under the party frames, moves and resizes by its handles, keeps its place, goes back to it from the options,
/// and stays on a smaller canvas; and ticking allocates nothing.
/// </summary>
public class ChatPresenterTests
{
    private const string HudPath = "Assets/Prefabs/UI/PlayerHudCanvas.prefab";
    private const string ProjectActionsPath = "Assets/InputSystem_Actions.inputactions";

    private InputActionAsset actions;
    private PlayerInputService playerInput;
    private GameplayInputGate inputGate;
    private FakeMenuInput menuInput;
    private MenuStack menus;
    private GameObject hud;
    private ChatView view;
    private ManualClock clock;
    private ManualWallClock wallClock;
    private ChatLog log;
    private HudSettings hudSettings;
    private UserSettings settings;
    private FakeSessionStatus session;
    private FakeChatInput chatInput;
    private FakeChatSender sender;
    private ChatPresenter presenter;

    /// <summary>
    /// Lays a copy of the HUD out at 640 x 360 UI px and starts a presenter on its chat, online, with an empty log.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        actions = InputActionAsset.FromJson(AssetDatabase.LoadAssetAtPath<InputActionAsset>(ProjectActionsPath).ToJson());
        playerInput = new PlayerInputService(actions, null);
        inputGate = new GameplayInputGate(new GameFlow(null, null, null, null), playerInput);
        menuInput = new FakeMenuInput();
        menus = new MenuStack(menuInput);
        hud = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath));
        hud.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        Resize(640, 360);
        view = hud.GetComponentInChildren<ChatView>(true);
        view.Subscribe();
        clock = new ManualClock();
        wallClock = new ManualWallClock();
        log = new ChatLog(clock, wallClock);
        hudSettings = HudSettings.Create();
        settings = new UserSettings(new MemorySaveStore());
        session = new FakeSessionStatus { IsActive = true };
        chatInput = new FakeChatInput();
        sender = new FakeChatSender();
        presenter = new ChatPresenter(view, log, hudSettings, clock, chatInput, inputGate, menus, session, settings, sender, new UISelection(null));
        presenter.Start();
    }

    /// <summary>
    /// Disposes the presenter and the input, and destroys the HUD, the settings and the copied actions.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        presenter.Dispose();
        view.Unsubscribe();
        Object.DestroyImmediate(hud);
        Object.DestroyImmediate(hudSettings);
        playerInput.Dispose();
        Object.DestroyImmediate(actions);
    }

    /// <summary>
    /// Offline the chat does not show and its key opens nothing; online it shows.
    /// </summary>
    [Test]
    public void Offline_TheChatHides_AndOnlineItShows()
    {
        Assert.That(view.IsShown, Is.True);
        session.IsActive = false;
        chatInput.ChatPressedThisFrame = true;
        presenter.Tick();
        Assert.That(view.IsShown, Is.False);
        Assert.That(presenter.IsOpen, Is.False);

        chatInput.ChatPressedThisFrame = false;
        session.IsActive = true;
        presenter.Tick();
        Assert.That(view.IsShown, Is.True);
    }

    /// <summary>
    /// The Chat key opens the window: its panel and field show, the chat takes the pointer, gameplay input is off and
    /// the chat is the top menu, so Escape closes it, giving everything back and sending nothing.
    /// </summary>
    [Test]
    public void TheChatKey_OpensTheWindow_TurningGameplayOff_AndEscapeClosesIt()
    {
        Open();
        Assert.That(presenter.IsOpen, Is.True);
        Assert.That(view.IsOpen, Is.True);
        Assert.That(view.Field.gameObject.activeSelf, Is.True);
        Assert.That(view.GetComponent<CanvasGroup>().blocksRaycasts, Is.True);
        Assert.That(inputGate.IsBlocked, Is.True);
        Assert.That(menus.Top, Is.SameAs(presenter));

        menuInput.BackPressedThisFrame = true;
        menus.Tick();
        menuInput.BackPressedThisFrame = false;
        Assert.That(presenter.IsOpen, Is.False);
        Assert.That(view.Field.gameObject.activeSelf, Is.False);
        Assert.That(view.GetComponent<CanvasGroup>().blocksRaycasts, Is.False, "At rest the chat never takes the pointer.");
        Assert.That(inputGate.IsBlocked, Is.False);
        Assert.That(menus.Count, Is.Zero);
        Assert.That(sender.Sent, Is.Empty);
    }

    /// <summary>
    /// Enter sends the field's line, cleaned, and closes the window; an empty line closes it without sending.
    /// </summary>
    [Test]
    public void Enter_SendsTheCleanedLine_AndAnEmptyLineJustCloses()
    {
        Open();
        presenter.Tick();
        presenter.Submit("  on my way\u4E00 ");
        Assert.That(sender.Sent, Is.EqualTo(new[] { "on my way" }));
        Assert.That(presenter.IsOpen, Is.False);
        Assert.That(inputGate.IsBlocked, Is.False);

        presenter.Tick();
        Open();
        presenter.Tick();
        presenter.Submit("   ");
        Assert.That(presenter.IsOpen, Is.False);
        Assert.That(sender.Sent.Count, Is.EqualTo(1));
    }

    /// <summary>
    /// The press of the key that opens the window does not also submit it, and the press that closes it cannot open it
    /// again in the next tick.
    /// </summary>
    [Test]
    public void TheKeysOwnPress_NeitherSubmitsTheWindow_NorReopensIt()
    {
        chatInput.ChatPressedThisFrame = true;
        presenter.Tick();
        presenter.Submit(string.Empty);
        Assert.That(presenter.IsOpen, Is.True, "The opening press's submit is ignored.");

        presenter.Tick();
        presenter.Submit("hi");
        Assert.That(presenter.IsOpen, Is.False);
        presenter.Tick();
        Assert.That(presenter.IsOpen, Is.False, "Not opened again by the press that closed it.");
        presenter.Tick();
        Assert.That(presenter.IsOpen, Is.True, "The key still held opens it a tick later.");
        chatInput.ChatPressedThisFrame = false;
    }

    /// <summary>
    /// The newest line shows at the bottom: a player's as "Player 2: on my way", its name in its slot's colour, the
    /// party's in TextMuted, a typed tag escaped; with timestamps on each starts with its time in TextMuted.
    /// </summary>
    [Test]
    public void Lines_ShowNewestFirst_InTheirSlotsColours_ThePartysMuted_WithTimestampsWhenAsked()
    {
        UiTheme theme = AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/Data/UI/UiTheme.asset");
        string muted = "#" + ColorUtility.ToHtmlStringRGB(theme.Colour(UiColour.TextMuted));
        string violet = "#" + ColorUtility.ToHtmlStringRGB(theme.Colour(UiColour.Player2));
        Assert.That(view.SlotColour(2), Is.EqualTo(violet));
        Assert.That(view.MutedColour, Is.EqualTo(muted));

        log.AddPlayerLine(2, "on my <b>way</b>");
        log.AddPartyLine("Player 3 joined");
        presenter.Tick();
        Assert.That(presenter.ShownCount, Is.EqualTo(2));
        Assert.That(view.LineText(0), Is.EqualTo($"<color={muted}>Player 3 joined</color>"));
        Assert.That(view.LineText(1), Is.EqualTo($"<color={violet}>Player 2</color>: on my <\u200Bb>way<\u200B/b>"));

        settings.SetChatTimestamps(true);
        presenter.Tick();
        string stamp = log[0].Stamp;
        Assert.That(stamp, Does.Match(@"^\d\d:\d\d$"));
        Assert.That(view.LineText(0), Is.EqualTo($"<color={muted}>{stamp} Player 3 joined</color>"));
        Assert.That(view.LineText(1), Is.EqualTo($"<color={muted}>{stamp}</color> <color={violet}>Player 2</color>: on my <\u200Bb>way<\u200B/b>"));
    }

    /// <summary>
    /// At rest a line shows at full for 10 s, then fades over 1 s; opening the window shows it at full again, and
    /// closing it lets it fade again.
    /// </summary>
    [Test]
    public void ALine_ShowsForItsTime_ThenFades_AndOpeningShowsItAgain()
    {
        log.AddPlayerLine(1, "heading north");
        presenter.Tick();
        Assert.That(view.LineAlpha(0), Is.EqualTo(1f));
        Advance(10f);
        Assert.That(view.LineAlpha(0), Is.EqualTo(1f));
        Advance(0.5f);
        Assert.That(view.LineAlpha(0), Is.EqualTo(0.5f).Within(0.001f));
        Advance(0.5f);
        Assert.That(view.LineAlpha(0), Is.Zero);

        Open();
        Assert.That(view.LineAlpha(0), Is.EqualTo(1f), "Open, every line shows again.");
        menuInput.BackPressedThisFrame = true;
        menus.Tick();
        menuInput.BackPressedThisFrame = false;
        presenter.Tick();
        Assert.That(view.LineAlpha(0), Is.Zero);
    }

    /// <summary>
    /// Only whole lines show, as many as fit between the header and the field, the newest first: six one-line ones in
    /// the default window, and a long line wraps to more lines, leaving room for fewer.
    /// </summary>
    [Test]
    public void OnlyWholeLines_ShowThatFit_TheNewestFirst()
    {
        for (int i = 0; i < 20; i++)
        {
            log.AddPlayerLine(1, $"line {i}");
        }

        presenter.Tick();
        Assert.That(presenter.ShownCount, Is.EqualTo(6), "66 UI px of lines at 11 each.");
        Assert.That(view.LineText(0), Does.EndWith("line 19"));
        Assert.That(view.LineText(5), Does.EndWith("line 14"));
        Assert.That(view.LineText(6), Is.Null);

        log.AddPlayerLine(3, new string('w', 20) + " " + new string('w', 20) + " " + new string('w', 20));
        presenter.Tick();
        Assert.That(view.RowsOf(0), Is.GreaterThan(1), "A long line wraps.");
        Assert.That(presenter.ShownCount, Is.EqualTo(6 - view.RowsOf(0) + 1));
    }

    /// <summary>
    /// The window stands under the party frames. Dragging its header moves it and its grip resizes it, no larger than the
    /// largest; the place is kept when the drag ends. Put back from the options, it stands in its place again, and one
    /// dragged back to its place is kept as never moved.
    /// </summary>
    [Test]
    public void TheWindow_MovesAndResizesByItsHandles_AndKeepsItsPlace()
    {
        Assert.That(view.Placed, Is.EqualTo(new RectInt(8, 98, 144, 96)));
        Open();

        Drag(view.MoveHandle, new Vector2(30f, -20f));
        Assert.That(view.Placed, Is.EqualTo(new RectInt(38, 118, 144, 96)));
        Assert.That(settings.ChatWindow.IsMoved, Is.True);
        Assert.That(settings.ChatWindow.Corner, Is.EqualTo(ScreenCorner.TopLeft));
        Assert.That(settings.ChatWindow.Offset, Is.EqualTo(new Vector2Int(38, 118)));

        Drag(view.ResizeHandle, new Vector2(500f, -500f));
        Assert.That(view.Placed, Is.EqualTo(new RectInt(38, 118, 240, 160)));
        Assert.That(settings.ChatWindow.Size, Is.EqualTo(new Vector2Int(240, 160)));

        settings.SetChatWindow(default);
        Assert.That(view.Placed, Is.EqualTo(new RectInt(8, 98, 144, 96)), "Put back from the options.");

        Drag(view.MoveHandle, new Vector2(30f, 0f));
        Drag(view.MoveHandle, new Vector2(-30f, 0f));
        Assert.That(view.Placed, Is.EqualTo(new RectInt(8, 98, 144, 96)));
        Assert.That(settings.ChatWindow.IsMoved, Is.False, "Dragged back to its place, it is kept as never moved.");
    }

    /// <summary>
    /// A window kept near the bottom right corner keeps its distance from it when the canvas shrinks to 480 x 270.
    /// </summary>
    [Test]
    public void AMovedWindow_KeepsItsPlace_OnASmallerCanvas()
    {
        settings.SetChatWindow(new ChatWindowPlace(ScreenCorner.BottomRight, new Vector2Int(8, 80), new Vector2Int(200, 100)));
        Assert.That(view.Placed, Is.EqualTo(new RectInt(640 - 208, 360 - 180, 200, 100)));

        Resize(480, 270);
        presenter.Tick();
        Assert.That(view.Placed, Is.EqualTo(new RectInt(480 - 208, 270 - 180, 200, 100)));
    }

    /// <summary>
    /// Ticking with lines shown and fading allocates nothing.
    /// </summary>
    [Test]
    public void Ticking_AllocatesNothing()
    {
        log.AddPlayerLine(2, "on my way");
        log.AddPartyLine("Player 2 joined");
        presenter.Tick();
        clock.Advance(10.2f);
        presenter.Tick();

        Assert.That(() =>
        {
            clock.Advance(0.1f);
            presenter.Tick();
        }, Is.Not.AllocatingGCMemory());
    }

    /// <summary>
    /// Presses the Chat key for one tick.
    /// </summary>
    private void Open()
    {
        chatInput.ChatPressedThisFrame = true;
        presenter.Tick();
        chatInput.ChatPressedThisFrame = false;
    }

    /// <summary>
    /// Runs the clock on by <paramref name="seconds"/> and ticks.
    /// </summary>
    private void Advance(float seconds)
    {
        clock.Advance(seconds);
        presenter.Tick();
    }

    /// <summary>
    /// Drags <paramref name="handle"/> with the left button by <paramref name="screen"/> screen pixels, y going up, at a
    /// scale of 1, and lets it go.
    /// </summary>
    private static void Drag(WindowDragHandle handle, Vector2 screen)
    {
        PointerEventData pointer = new(null)
        {
            button = PointerEventData.InputButton.Left,
            pressPosition = new Vector2(200f, 200f),
            position = new Vector2(200f, 200f),
        };
        handle.OnBeginDrag(pointer);
        pointer.position += screen;
        handle.OnDrag(pointer);
        handle.OnEndDrag(pointer);
    }

    /// <summary>
    /// Sizes the HUD's world-space canvas to <paramref name="width"/> x <paramref name="height"/> UI px.
    /// </summary>
    private void Resize(int width, int height)
    {
        RectTransform root = (RectTransform)hud.transform;
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(width, height);
        root.localScale = Vector3.one;
    }
}
