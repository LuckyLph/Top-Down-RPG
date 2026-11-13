using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Sets the keyboard and gamepad selection on the Main scope's <see cref="EventSystem"/>, for menus that move focus
/// between their buttons. Does nothing without an event system.
/// </summary>
public sealed class UISelection
{
    private readonly EventSystem eventSystem;

    /// <summary>
    /// Creates the helper over <paramref name="eventSystem"/>, which may be null.
    /// </summary>
    public UISelection(EventSystem eventSystem)
    {
        this.eventSystem = eventSystem;
    }

    /// <summary>
    /// The object selected now, or null without an event system or a selection.
    /// </summary>
    public GameObject Current => eventSystem != null ? eventSystem.currentSelectedGameObject : null;

    /// <summary>
    /// Selects <paramref name="selectable"/>, or clears the selection when it is null.
    /// </summary>
    public void Select(Selectable selectable)
    {
        if (eventSystem != null)
        {
            eventSystem.SetSelectedGameObject(selectable != null ? selectable.gameObject : null);
        }
    }
}
