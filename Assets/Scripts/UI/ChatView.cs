using System;
using TMPro;
using UnityEngine;

/// <summary>
/// The HUD's chat (Docs/UiRevamp.md, Phase 12): a window the player moves by its header and resizes by the grip in its
/// bottom right corner while it is open, its lines newest at the bottom above the field, and a <c>Field</c> at its foot.
/// At rest only the lines show, outlined over the world, and nothing of it takes the pointer; open, the window's panel,
/// header and grips show behind them and the field takes the typing. The field keeps typed text to what a chat line may
/// hold (<see cref="ChatText"/>). Passive: <see cref="ChatPresenter"/> says what it shows and where it stands.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class ChatView : MonoBehaviour
{
    [SerializeField] private UiTheme theme;
    [SerializeField, Tooltip("The window behind the lines: its panel, header and grips, shown while the field is open.")]
    private GameObject window;
    [SerializeField, Tooltip("The window's header, which moves it.")]
    private WindowDragHandle moveHandle;
    [SerializeField, Tooltip("The grip in the window's bottom right corner, which resizes it.")]
    private WindowDragHandle resizeHandle;
    [SerializeField, Tooltip("The area the lines stand in, between the header and the field.")]
    private RectTransform linesArea;
    [SerializeField, Tooltip("The texts that show the lines, the newest first.")]
    private TMP_Text[] lines = new TMP_Text[0];
    [SerializeField, Tooltip("The field typed lines go in.")]
    private TMP_InputField field;
    [SerializeField, Tooltip("Takes the pointer only while the window is open.")]
    private CanvasGroup group;
    [SerializeField, Min(1f), Tooltip("UI px per line of text: Galmuri7's line height.")]
    private float lineHeight = 11f;

    private readonly string[] slotColours = new string[PlayerSlots.Count + 1];
    private RectTransform rect;
    private RectTransform canvasRoot;
    private float[] alphas = new float[0];
    private string mutedColour = "#FFFFFF";
    private bool prepared;
    private bool subscribed;

    public bool IsShown => gameObject.activeSelf;
    public bool IsOpen { get; private set; }
    public int LineCapacity => lines != null ? lines.Length : 0;
    public float LineHeight => lineHeight;
    public RectInt Placed { get; private set; }
    public TMP_InputField Field => field;
    public bool FieldFocused => field != null && field.isFocused;
    public string FieldText => field != null ? field.text : string.Empty;
    public string MutedColour
    {
        get
        {
            EnsurePrepared();
            return mutedColour;
        }
    }

    internal WindowDragHandle MoveHandle => moveHandle;
    internal WindowDragHandle ResizeHandle => resizeHandle;

    /// <summary>
    /// Raised when the field submits its text, Enter pressed.
    /// </summary>
    public event Action<string> Submitted;

    /// <summary>
    /// Raised when a drag of the window's header or grip begins.
    /// </summary>
    public event Action<WindowDrag> DragBegan;

    /// <summary>
    /// Raised as the window's header or grip is dragged, with how far the pointer went since the drag began, in UI px,
    /// y going down.
    /// </summary>
    public event Action<WindowDrag, Vector2Int> Dragged;

    /// <summary>
    /// Raised when a drag of the window's header or grip ends.
    /// </summary>
    public event Action<WindowDrag> DragEnded;

    /// <summary>
    /// Caches its rect and the slots' colours, keeps the field to a chat line's characters and length, and logs an
    /// error for each missing reference.
    /// </summary>
    private void Awake()
    {
        rect = (RectTransform)transform;
        if (theme == null || window == null || linesArea == null || field == null || group == null || lines == null || lines.Length == 0)
        {
            Debug.LogError($"{name} is missing its theme, window, lines area, lines, field or canvas group.", this);
        }

        EnsurePrepared();
    }

    /// <summary>
    /// Follows the field's submits and the window's handles.
    /// </summary>
    private void OnEnable()
    {
        Subscribe();
    }

    /// <summary>
    /// Stops following the field and the handles.
    /// </summary>
    private void OnDisable()
    {
        Unsubscribe();
    }

    /// <summary>
    /// The colour of slot <paramref name="slot"/>'s name, as a rich text colour, "#C084FC".
    /// </summary>
    public string SlotColour(int slot)
    {
        EnsurePrepared();
        return PlayerSlots.IsSlot(slot) ? slotColours[slot] : mutedColour;
    }

    /// <summary>
    /// Shows or hides the whole chat.
    /// </summary>
    public void SetShown(bool shown)
    {
        if (gameObject.activeSelf != shown)
        {
            gameObject.SetActive(shown);
        }
    }

    /// <summary>
    /// Shows the window and the field, and lets the chat take the pointer, while it is <paramref name="open"/>.
    /// </summary>
    public void SetOpen(bool open)
    {
        IsOpen = open;
        if (window != null && window.activeSelf != open)
        {
            window.SetActive(open);
        }

        if (field != null && field.gameObject.activeSelf != open)
        {
            field.gameObject.SetActive(open);
        }

        if (group != null)
        {
            group.blocksRaycasts = open;
            group.interactable = open;
        }
    }

    /// <summary>
    /// The size of the canvas the chat is drawn on, in whole UI px; the root canvas is found once.
    /// </summary>
    public Vector2Int CanvasSize
    {
        get
        {
            if (canvasRoot == null)
            {
                Canvas canvas = GetComponentInParent<Canvas>();
                canvasRoot = canvas != null ? (RectTransform)canvas.rootCanvas.transform : transform.parent as RectTransform;
            }

            Vector2 size = canvasRoot != null ? canvasRoot.rect.size : Vector2.zero;
            return new Vector2Int(Mathf.RoundToInt(size.x), Mathf.RoundToInt(size.y));
        }
    }

    /// <summary>
    /// Stands the chat at <paramref name="window"/>, in UI px from the canvas's top left corner, y going down. Returns
    /// whether its size changed.
    /// </summary>
    public bool Place(RectInt windowRect)
    {
        bool resized = windowRect.size != Placed.size;
        Placed = windowRect;
        if (rect == null)
        {
            rect = (RectTransform)transform;
        }

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(windowRect.x, -windowRect.y);
        rect.sizeDelta = new Vector2(windowRect.width, windowRect.height);
        return resized;
    }

    /// <summary>
    /// Shows the first <paramref name="count"/> of <paramref name="texts"/> (rich text, the newest first) from the
    /// bottom of the lines area up, each wrapped to as many lines as it needs, as many whole ones as fit; hides the
    /// rest. Returns how many show.
    /// </summary>
    public int ShowLines(string[] texts, int count)
    {
        EnsurePrepared();
        float room = linesArea != null ? linesArea.rect.height : 0f;
        float used = 0f;
        int shown = 0;
        for (int i = 0; i < LineCapacity; i++)
        {
            TMP_Text line = lines[i];
            if (line == null)
            {
                continue;
            }

            if (i >= count || shown < i)
            {
                Hide(i);
                continue;
            }

            if (!line.gameObject.activeSelf)
            {
                line.gameObject.SetActive(true);
                alphas[i] = float.NaN;
            }

            if (!string.Equals(line.text, texts[i], StringComparison.Ordinal))
            {
                line.text = texts[i];
            }

            line.ForceMeshUpdate();
            float height = Mathf.Max(1, line.textInfo.lineCount) * lineHeight;
            if (used + height > room + 0.01f)
            {
                Hide(i);
                continue;
            }

            RectTransform lineRect = line.rectTransform;
            lineRect.anchoredPosition = new Vector2(0f, used);
            lineRect.sizeDelta = new Vector2(0f, height);
            used += height;
            shown++;
        }

        return shown;
    }

    /// <summary>
    /// How many lines of text the line at <paramref name="index"/> wraps to, 0 while it is hidden, for tests.
    /// </summary>
    internal int RowsOf(int index)
    {
        TMP_Text line = lines[index];
        return line != null && line.gameObject.activeSelf ? Mathf.Max(1, line.textInfo.lineCount) : 0;
    }

    /// <summary>
    /// The text the line at <paramref name="index"/> shows, the newest at 0; null while it is hidden.
    /// </summary>
    public string LineText(int index)
    {
        TMP_Text line = index >= 0 && index < LineCapacity ? lines[index] : null;
        return line != null && line.gameObject.activeSelf ? line.text : null;
    }

    /// <summary>
    /// The opacity the line at <paramref name="index"/> was last given.
    /// </summary>
    public float LineAlpha(int index)
    {
        return index >= 0 && index < alphas.Length && !float.IsNaN(alphas[index]) ? alphas[index] : 1f;
    }

    /// <summary>
    /// Draws the line at <paramref name="index"/> at <paramref name="alpha"/>, through its renderer, so its mesh is not
    /// built again. Does nothing when it already is.
    /// </summary>
    public void SetLineAlpha(int index, float alpha)
    {
        EnsurePrepared();
        if (index < 0 || index >= LineCapacity || lines[index] == null || alphas[index] == alpha)
        {
            return;
        }

        alphas[index] = alpha;
        lines[index].canvasRenderer.SetAlpha(alpha);
    }

    /// <summary>
    /// Empties the field and has it take the typing.
    /// </summary>
    public void OpenField()
    {
        if (field == null)
        {
            return;
        }

        field.text = string.Empty;
        FocusField();
    }

    /// <summary>
    /// Has the field take the typing again, the caret at the end of what it holds.
    /// </summary>
    public void FocusField()
    {
        if (field != null && field.gameObject.activeInHierarchy)
        {
            field.ActivateInputField();
            field.MoveTextEnd(false);
        }
    }

    /// <summary>
    /// Stops the field taking the typing and empties it.
    /// </summary>
    public void CloseField()
    {
        if (field == null)
        {
            return;
        }

        field.DeactivateInputField();
        field.text = string.Empty;
    }

    /// <summary>
    /// Assigns the references in code, for tests that build the view without a prefab, and prepares them as
    /// <see cref="Awake"/> does.
    /// </summary>
    internal void ConfigureReferences(UiTheme uiTheme, GameObject windowObject, WindowDragHandle move, WindowDragHandle resize, RectTransform area, TMP_Text[] lineTexts, TMP_InputField inputField, CanvasGroup canvasGroup)
    {
        Unsubscribe();
        theme = uiTheme;
        window = windowObject;
        moveHandle = move;
        resizeHandle = resize;
        linesArea = area;
        lines = lineTexts;
        field = inputField;
        group = canvasGroup;
        rect = (RectTransform)transform;
        prepared = false;
        EnsurePrepared();
        if (isActiveAndEnabled)
        {
            Subscribe();
        }
    }

    /// <summary>
    /// Once, as it wakes or is first used, which an edit-mode copy is without waking: builds the slots' colours from the
    /// theme, hides every line, and keeps the field to a chat line.
    /// </summary>
    internal void EnsurePrepared()
    {
        if (prepared)
        {
            return;
        }

        prepared = true;
        if (theme != null)
        {
            mutedColour = "#" + ColorUtility.ToHtmlStringRGB(theme.Colour(UiColour.TextMuted));
            for (int slot = 1; slot <= PlayerSlots.Count; slot++)
            {
                slotColours[slot] = "#" + ColorUtility.ToHtmlStringRGB(theme.Colour(PlayerSlots.Role(slot)));
            }
        }

        alphas = new float[LineCapacity];
        for (int i = 0; i < LineCapacity; i++)
        {
            alphas[i] = float.NaN;
            Hide(i);
        }

        if (field != null)
        {
            field.characterLimit = ChatText.MaxLength;
            field.onValidateInput = ChatText.Validate;
        }
    }

    /// <summary>
    /// Hides the line at <paramref name="index"/>.
    /// </summary>
    private void Hide(int index)
    {
        TMP_Text line = lines[index];
        if (line != null && line.gameObject.activeSelf)
        {
            line.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Follows the field's submits and the handles' drags, once; tests call it on an edit-mode copy, which the engine
    /// does not enable.
    /// </summary>
    internal void Subscribe()
    {
        if (subscribed)
        {
            return;
        }

        subscribed = true;
        if (field != null)
        {
            field.onSubmit.AddListener(HandleSubmit);
        }

        foreach (WindowDragHandle handle in new[] { moveHandle, resizeHandle })
        {
            if (handle != null)
            {
                handle.Began += HandleBegan;
                handle.Dragged += HandleDragged;
                handle.Ended += HandleEnded;
            }
        }
    }

    /// <summary>
    /// Stops following the field and the handles.
    /// </summary>
    internal void Unsubscribe()
    {
        if (!subscribed)
        {
            return;
        }

        subscribed = false;
        if (field != null)
        {
            field.onSubmit.RemoveListener(HandleSubmit);
        }

        foreach (WindowDragHandle handle in new[] { moveHandle, resizeHandle })
        {
            if (handle != null)
            {
                handle.Began -= HandleBegan;
                handle.Dragged -= HandleDragged;
                handle.Ended -= HandleEnded;
            }
        }
    }

    /// <summary>
    /// Passes the field's submit on.
    /// </summary>
    private void HandleSubmit(string text)
    {
        Submitted?.Invoke(text);
    }

    /// <summary>
    /// Passes a handle's drag start on.
    /// </summary>
    private void HandleBegan(WindowDrag kind)
    {
        DragBegan?.Invoke(kind);
    }

    /// <summary>
    /// Passes a handle's drag on.
    /// </summary>
    private void HandleDragged(WindowDrag kind, Vector2Int delta)
    {
        Dragged?.Invoke(kind, delta);
    }

    /// <summary>
    /// Passes a handle's drag end on.
    /// </summary>
    private void HandleEnded(WindowDrag kind)
    {
        DragEnded?.Invoke(kind);
    }
}
