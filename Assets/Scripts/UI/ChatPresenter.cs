using System;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point for the HUD's chat (Docs/UiRevamp.md, Phase 12), on this machine only. It shows only
/// online, where there is someone to talk to.
/// <list type="bullet">
/// <item><b>Lines.</b> The newest lines of the <see cref="ChatLog"/>, newest at the bottom, as many whole ones as the
/// window holds: a player's as "Player 2: on my way", the name in its slot's colour, and the party's in
/// <see cref="UiColour.TextMuted"/>; with the Chat timestamps preference on, each starts with the time it arrived,
/// "14:32", in <see cref="UiColour.TextMuted"/>. At rest a line shows for <see cref="HudSettings.ChatLineSeconds"/> of
/// game time, then fades over <see cref="HudSettings.ChatFadeSeconds"/>; open, every line shows again.</item>
/// <item><b>Typing.</b> The Chat key (<see cref="IChatInput"/>) opens the window: its field takes the typing, gameplay
/// input is off (<see cref="GameplayInputGate"/>) and the chat is a layer on the <see cref="MenuStack"/>, so Escape
/// closes it rather than opening the in-game menu. Enter sends the field's line, cleaned, through the
/// <see cref="IChatSender"/> and closes it; an empty line just closes it. A submit in the tick the window opened is the
/// key that opened it and is ignored, and the key cannot open the window again in the tick after it closed. While open,
/// the field takes the typing back whenever it lost it, as dragging the window does.</item>
/// <item><b>The window.</b> It stands where <see cref="UserSettings.ChatWindow"/> says, or at its default place under the
/// party frames, laid out by <see cref="ChatWindowLayout"/>. Dragging its header moves it and its grip resizes it; the
/// place is kept when the drag ends, a window dragged back to its default place kept as never moved. It is placed again
/// when the canvas changes size or the preference changes.</item>
/// </list>
/// Lines are formatted only when the log, the window's size or the timestamps change, so <see cref="Tick"/> allocates
/// nothing.
/// </summary>
public sealed class ChatPresenter : IStartable, ITickable, IDisposable, IMenuLayer
{
    private readonly ChatView view;
    private readonly ChatLog log;
    private readonly HudSettings hudSettings;
    private readonly IClock clock;
    private readonly IChatInput input;
    private readonly GameplayInputGate inputGate;
    private readonly MenuStack menus;
    private readonly ISessionStatus session;
    private readonly UserSettings settings;
    private readonly IChatSender sender;
    private readonly UISelection selection;
    private ChatWindowLayout layout;
    private string[] texts = new string[0];
    private float[] times = new float[0];
    private int shown;
    private Vector2Int canvas;
    private RectInt dragStart;
    private bool dragging;
    private bool dirty;
    private bool open;
    private bool justOpened;
    private bool closedSinceTick;
    private bool wasShown;
    private bool started;

    /// <summary>
    /// Captures the chat's view and log, its times and window from the HUD's settings, the game clock, the chat key,
    /// the gameplay input gate and menu stack it holds while open, the session it shows in, the local preferences, the
    /// sender of the local player's lines and the UI selection its field takes.
    /// </summary>
    public ChatPresenter(
        ChatView view,
        ChatLog log,
        HudSettings hudSettings,
        IClock clock,
        IChatInput input,
        GameplayInputGate inputGate,
        MenuStack menus,
        ISessionStatus session,
        UserSettings settings,
        IChatSender sender,
        UISelection selection)
    {
        this.view = view;
        this.log = log;
        this.hudSettings = hudSettings;
        this.clock = clock;
        this.input = input;
        this.inputGate = inputGate;
        this.menus = menus;
        this.session = session;
        this.settings = settings;
        this.sender = sender;
        this.selection = selection;
    }

    /// <summary>
    /// Whether the window is open for typing.
    /// </summary>
    public bool IsOpen => open;

    /// <summary>
    /// How many lines show, for tests.
    /// </summary>
    internal int ShownCount => shown;

    /// <summary>
    /// The window's layout, for tests.
    /// </summary>
    internal ChatWindowLayout Layout => layout;

    /// <summary>
    /// Follows the log, the preferences, the field and the window's handles, places the window and shows the lines.
    /// </summary>
    public void Start()
    {
        if (view == null)
        {
            return;
        }

        layout = ChatWindowLayout.From(hudSettings);
        texts = new string[view.LineCapacity];
        times = new float[view.LineCapacity];
        log.Added += HandleAdded;
        log.Cleared += MarkDirty;
        settings.ChatTimestampsChanged += MarkDirty;
        settings.ChatWindowChanged += HandleWindowChanged;
        view.Submitted += Submit;
        view.DragBegan += HandleDragBegan;
        view.Dragged += HandleDragged;
        view.DragEnded += HandleDragEnded;
        started = true;
        view.SetOpen(false);
        canvas = view.CanvasSize;
        PlaceWindow();
        dirty = true;
        Tick();
    }

    /// <summary>
    /// Stops following, and gives gameplay input and the menu stack back when the window is open.
    /// </summary>
    public void Dispose()
    {
        if (!started)
        {
            return;
        }

        started = false;
        log.Added -= HandleAdded;
        log.Cleared -= MarkDirty;
        settings.ChatTimestampsChanged -= MarkDirty;
        settings.ChatWindowChanged -= HandleWindowChanged;
        view.Submitted -= Submit;
        view.DragBegan -= HandleDragBegan;
        view.Dragged -= HandleDragged;
        view.DragEnded -= HandleDragEnded;
        if (open)
        {
            open = false;
            menus.Remove(this);
            inputGate.Unblock(this);
        }
    }

    /// <summary>
    /// Shows the chat online only, follows the canvas's size, opens the window on the Chat key, keeps the field
    /// taking the typing while open, lays the lines out when they changed and fades them.
    /// </summary>
    public void Tick()
    {
        if (!started)
        {
            return;
        }

        justOpened = false;
        bool active = session != null && session.IsActive;
        view.SetShown(active);
        if (!active)
        {
            Close();
            closedSinceTick = false;
            wasShown = false;
            return;
        }

        if (!wasShown)
        {
            wasShown = true;
            dirty = true;
        }

        Vector2Int size = view.CanvasSize;
        if (size != canvas)
        {
            canvas = size;
            PlaceWindow();
        }

        if (!open && !closedSinceTick && input.ChatPressedThisFrame)
        {
            Open();
        }

        closedSinceTick = false;
        if (open && !justOpened && !view.FieldFocused)
        {
            view.FocusField();
        }

        if (dirty)
        {
            Refresh();
        }

        Fade();
    }

    /// <summary>
    /// Escape or gamepad East while the window is open: closes it without sending.
    /// </summary>
    public void HandleBack()
    {
        Close();
    }

    /// <summary>
    /// The field submitted <paramref name="text"/>: sends it, cleaned, unless it is empty, and closes the window.
    /// Ignored while closed and in the tick the window opened, whose key it is.
    /// </summary>
    public void Submit(string text)
    {
        if (!open || justOpened)
        {
            return;
        }

        string clean = ChatText.Clean(text);
        if (clean.Length > 0)
        {
            sender?.Send(clean);
        }

        Close();
    }

    /// <summary>
    /// <paramref name="line"/> as the chat shows it, rich text: a player's name in its slot's colour then its escaped
    /// message, or the party's line in <see cref="UiColour.TextMuted"/>, after the time it arrived with timestamps on.
    /// </summary>
    internal string Format(ChatLine line)
    {
        string muted = view.MutedColour;
        string text = ChatText.Escape(line.Text);
        bool stamps = settings.ChatTimestamps && line.Stamp.Length > 0;
        if (line.IsParty)
        {
            return stamps
                ? string.Concat("<color=", muted, ">", line.Stamp, " ", text, "</color>")
                : string.Concat("<color=", muted, ">", text, "</color>");
        }

        string name = string.Concat("<color=", view.SlotColour(line.Slot), ">", PlayerSlots.Label(line.Slot), "</color>: ", text);
        return stamps ? string.Concat("<color=", muted, ">", line.Stamp, "</color> ", name) : name;
    }

    /// <summary>
    /// The opacity of a line <paramref name="age"/> seconds old at rest: full for the line's time, then fading to none.
    /// </summary>
    internal float AlphaAt(float age)
    {
        float lineSeconds = hudSettings.ChatLineSeconds;
        if (age <= lineSeconds)
        {
            return 1f;
        }

        float fade = hudSettings.ChatFadeSeconds;
        return fade <= 0f ? 0f : Mathf.Clamp01(1f - (age - lineSeconds) / fade);
    }

    /// <summary>
    /// Opens the window for typing: the field takes the typing, gameplay input goes off and the chat goes on the menu
    /// stack.
    /// </summary>
    private void Open()
    {
        open = true;
        justOpened = true;
        inputGate.Block(this);
        menus.Push(this);
        view.SetOpen(true);
        selection?.Select(view.Field);
        view.OpenField();
        dirty = true;
    }

    /// <summary>
    /// Closes the window: the field lets go, gameplay input comes back and the chat leaves the menu stack.
    /// </summary>
    private void Close()
    {
        if (!open)
        {
            return;
        }

        open = false;
        closedSinceTick = true;
        view.CloseField();
        selection?.Select(null);
        view.SetOpen(false);
        menus.Remove(this);
        inputGate.Unblock(this);
        dirty = true;
    }

    /// <summary>
    /// Formats the newest lines and shows as many as fit.
    /// </summary>
    private void Refresh()
    {
        dirty = false;
        int count = Math.Min(log.Count, texts.Length);
        for (int i = 0; i < count; i++)
        {
            ChatLine line = log[log.Count - 1 - i];
            texts[i] = Format(line);
            times[i] = line.Time;
        }

        shown = view.ShowLines(texts, count);
    }

    /// <summary>
    /// Draws every shown line at full while open, and at its age's opacity at rest.
    /// </summary>
    private void Fade()
    {
        float now = clock.Time;
        for (int i = 0; i < shown; i++)
        {
            view.SetLineAlpha(i, open ? 1f : AlphaAt(now - times[i]));
        }
    }

    /// <summary>
    /// Stands the window where the preferences say on the current canvas, laying the lines out again when its size
    /// changed.
    /// </summary>
    private void PlaceWindow()
    {
        if (view.Place(layout.Resolve(settings.ChatWindow, canvas)))
        {
            dirty = true;
        }
    }

    /// <summary>
    /// A line arrived: lay the lines out again.
    /// </summary>
    private void HandleAdded(ChatLine line)
    {
        dirty = true;
    }

    /// <summary>
    /// Something the lines show changed: lay them out again.
    /// </summary>
    private void MarkDirty()
    {
        dirty = true;
    }

    /// <summary>
    /// The window's place changed in the preferences: stand it there, unless a drag is placing it.
    /// </summary>
    private void HandleWindowChanged()
    {
        if (!dragging)
        {
            PlaceWindow();
        }
    }

    /// <summary>
    /// A drag of the header or grip began: it counts from where the window stands now.
    /// </summary>
    private void HandleDragBegan(WindowDrag kind)
    {
        dragging = true;
        dragStart = view.Placed;
    }

    /// <summary>
    /// The header or grip moved <paramref name="delta"/> UI px since the drag began: moves or resizes the window.
    /// </summary>
    private void HandleDragged(WindowDrag kind, Vector2Int delta)
    {
        if (!dragging)
        {
            return;
        }

        RectInt placed = kind == WindowDrag.Move ? layout.Moved(dragStart, delta, canvas) : layout.Resized(dragStart, delta, canvas);
        if (view.Place(placed))
        {
            dirty = true;
        }
    }

    /// <summary>
    /// The drag ended: keeps the window's place, as never moved when it stands at its default place.
    /// </summary>
    private void HandleDragEnded(WindowDrag kind)
    {
        if (!dragging)
        {
            return;
        }

        dragging = false;
        RectInt placed = view.Placed;
        settings.SetChatWindow(placed.Equals(layout.Default(canvas)) ? default : layout.PlaceOf(placed, canvas));
    }
}
