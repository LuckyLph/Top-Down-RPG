using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Shows the <see cref="HudTooltip"/> for the <see cref="ITooltipSource"/> on its object (an ability slot, the weapon slot, a status icon)
/// while the pointer is over it or it is selected, and hides it when neither holds, the source has nothing to show or
/// the object is disabled (Docs/DesignSystem.md, Components: Tooltip). Pointer events need a raycast target on the
/// object, which also keeps the click from reaching the world. Local to this machine.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [SerializeField, Tooltip("The tooltip this object shows, on its canvas.")] private HudTooltip tooltip;
    [SerializeField, Tooltip("A rect the tooltip shows above too, so it never covers it: the command bar, for its slots and status icons. Empty for none.")]
    private RectTransform clearOf;

    private ITooltipSource source;
    private bool pointerOver;
    private bool selected;
    private bool subscribed;

    internal HudTooltip Tooltip => tooltip;

    /// <summary>
    /// Finds the source on this object; logs an error without a source or a tooltip.
    /// </summary>
    private void Awake()
    {
        ResolveSource();
        if (source == null || tooltip == null)
        {
            Debug.LogError($"{name} needs a tooltip and a tooltip source on its object.", this);
        }
    }

    /// <summary>
    /// Follows the source's changes while enabled.
    /// </summary>
    private void OnEnable()
    {
        ResolveSource();
        if (source != null && !subscribed)
        {
            source.TooltipChanged += Refresh;
            subscribed = true;
        }
    }

    /// <summary>
    /// Stops following the source and hides the tooltip it shows, since a hidden object is neither pointed at nor
    /// selected.
    /// </summary>
    private void OnDisable()
    {
        if (source != null && subscribed)
        {
            source.TooltipChanged -= Refresh;
        }

        subscribed = false;
        pointerOver = false;
        selected = false;
        Refresh();
    }

    /// <summary>
    /// Shows the tooltip while the pointer is over the object.
    /// </summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerOver = true;
        Refresh();
    }

    /// <summary>
    /// Hides the tooltip when the pointer leaves, unless the object is selected.
    /// </summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        pointerOver = false;
        Refresh();
    }

    /// <summary>
    /// Shows the tooltip while the object is selected.
    /// </summary>
    public void OnSelect(BaseEventData eventData)
    {
        selected = true;
        Refresh();
    }

    /// <summary>
    /// Hides the tooltip when the object is deselected, unless the pointer is over it.
    /// </summary>
    public void OnDeselect(BaseEventData eventData)
    {
        selected = false;
        Refresh();
    }

    /// <summary>
    /// Sets the tooltip and the rect it keeps clear in code, for tests that build the trigger without a prefab.
    /// </summary>
    internal void Configure(HudTooltip shownTooltip, RectTransform keptClear = null)
    {
        tooltip = shownTooltip;
        clearOf = keptClear;
        ResolveSource();
    }

    /// <summary>
    /// Shows the source's title, numbers and description over this object while it is pointed at or selected and has
    /// something to show; hides the tooltip otherwise, when this object shows it.
    /// </summary>
    private void Refresh()
    {
        if (tooltip == null)
        {
            return;
        }

        RectTransform target = (RectTransform)transform;
        if ((pointerOver || selected) && source != null && source.TryGetTooltip(out string title, out string details, out string description))
        {
            tooltip.Show(target, clearOf, title, details, description);
        }
        else
        {
            tooltip.Hide(target);
        }
    }

    /// <summary>
    /// Finds the tooltip source on this object.
    /// </summary>
    private void ResolveSource()
    {
        if (source == null)
        {
            source = GetComponent<ITooltipSource>();
        }
    }
}
