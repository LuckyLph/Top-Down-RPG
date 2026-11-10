using UnityEngine;
using UnityEngine.UI;
using VContainer;

/// <summary>
/// On each UI canvas (Docs/DesignSystem.md, Foundations: Scale): keeps its <see cref="CanvasScaler"/> at a constant
/// pixel size with <see cref="ReferencePixelsPerUnit"/>, so one sprite pixel is one UI px, and its scale factor at the
/// <see cref="UiScale"/>'s zoom, once injected and on every change. Injected through its scope's auto-inject objects;
/// logs an error from <c>Start</c> without a zoom and keeps the scaler as it was saved.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasScaler))]
public sealed class UiPixelScale : MonoBehaviour
{
    public const float ReferencePixelsPerUnit = 32f;

    private CanvasScaler scaler;
    private UiScale scale;
    private bool following;

    /// <summary>
    /// Receives the zoom to follow, and follows it at once when enabled.
    /// </summary>
    [Inject]
    public void Construct(UiScale uiScale)
    {
        scale = uiScale;
        if (enabled && gameObject.activeInHierarchy)
        {
            Follow();
        }
    }

    /// <summary>
    /// Follows the zoom again once injected.
    /// </summary>
    private void OnEnable()
    {
        if (scale != null)
        {
            Follow();
        }
    }

    /// <summary>
    /// Logs an error when the scope never injected the zoom.
    /// </summary>
    private void Start()
    {
        if (scale == null)
        {
            Debug.LogError($"{name} has no UiScale: list it in its scope's auto-inject objects.", this);
        }
    }

    /// <summary>
    /// Stops following the zoom.
    /// </summary>
    private void OnDisable()
    {
        if (following)
        {
            scale.Changed -= Apply;
            following = false;
        }
    }

    /// <summary>
    /// Subscribes to the zoom's changes once and applies it now.
    /// </summary>
    private void Follow()
    {
        if (!following)
        {
            scale.Changed += Apply;
            following = true;
        }

        Apply();
    }

    /// <summary>
    /// Sets the scaler to a constant pixel size at <see cref="ReferencePixelsPerUnit"/> and the zoom.
    /// </summary>
    private void Apply()
    {
        if (scaler == null)
        {
            scaler = GetComponent<CanvasScaler>();
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.referencePixelsPerUnit = ReferencePixelsPerUnit;
        scaler.scaleFactor = scale.Factor;
    }
}
