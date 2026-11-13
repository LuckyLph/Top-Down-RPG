using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// A window's handle the pointer drags (Docs/DesignSystem.md, Components: Window): its header to move it, the grip in
/// its bottom right corner to resize it. While the left button drags it, it reports how far the pointer has gone since
/// it was pressed, in whole UI px of its root canvas with y going down, so a view can apply it to where the window
/// stood when the drag began without drifting. Passive: it moves nothing itself.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class WindowDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField, Tooltip("What dragging this handle does to its window.")]
    private WindowDrag kind;

    private Canvas canvas;
    private Vector2 start;
    private bool dragging;

    public WindowDrag Kind => kind;
    public bool IsDragging => dragging;

    /// <summary>
    /// Raised when a drag of this handle begins.
    /// </summary>
    public event Action<WindowDrag> Began;

    /// <summary>
    /// Raised as the pointer moves during a drag, with how far it went since it was pressed, in UI px, y going down.
    /// </summary>
    public event Action<WindowDrag, Vector2Int> Dragged;

    /// <summary>
    /// Raised when a drag ends, or the handle is disabled during one.
    /// </summary>
    public event Action<WindowDrag> Ended;

    /// <summary>
    /// Finds the canvas the handle is drawn on.
    /// </summary>
    private void Awake()
    {
        canvas = GetComponentInParent<Canvas>();
    }

    /// <summary>
    /// Ends a drag still running.
    /// </summary>
    private void OnDisable()
    {
        Finish();
    }

    /// <summary>
    /// Starts a drag of the left button from where it was pressed.
    /// </summary>
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        start = eventData.pressPosition;
        dragging = true;
        Began?.Invoke(kind);
        OnDrag(eventData);
    }

    /// <summary>
    /// Reports how far the pointer went since it was pressed.
    /// </summary>
    public void OnDrag(PointerEventData eventData)
    {
        if (!dragging)
        {
            return;
        }

        float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
        Dragged?.Invoke(kind, ToUiPixels(eventData.position - start, scale));
    }

    /// <summary>
    /// Ends the drag.
    /// </summary>
    public void OnEndDrag(PointerEventData eventData)
    {
        Finish();
    }

    /// <summary>
    /// <paramref name="screenDelta"/>, in screen pixels with y going up, in whole UI px of a canvas drawn at
    /// <paramref name="scale"/> screen pixels per UI px, with y going down.
    /// </summary>
    internal static Vector2Int ToUiPixels(Vector2 screenDelta, float scale)
    {
        float perPixel = scale > 0f ? scale : 1f;
        return new Vector2Int(Mathf.RoundToInt(screenDelta.x / perPixel), Mathf.RoundToInt(-screenDelta.y / perPixel));
    }

    /// <summary>
    /// Ends a running drag and says so.
    /// </summary>
    private void Finish()
    {
        if (!dragging)
        {
            return;
        }

        dragging = false;
        Ended?.Invoke(kind);
    }
}
