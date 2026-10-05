using UnityEngine;

/// <summary>
/// Passive row of <see cref="StatusIconView"/>s on the player HUD, above the ability bar.
/// </summary>
[DisallowMultipleComponent]
public class StatusBarView : MonoBehaviour
{
    [SerializeField] private StatusIconView[] icons = new StatusIconView[0];

    public int Capacity => icons != null ? icons.Length : 0;

    /// <summary>
    /// Logs an error when no icon views are assigned; otherwise hides them all.
    /// </summary>
    private void Awake()
    {
        if (Capacity == 0)
        {
            Debug.LogError($"{name} has no status icon views.", this);
            return;
        }

        Clear();
    }

    /// <summary>
    /// Shows <paramref name="definition"/> with <paramref name="stacks"/> on icon <paramref name="index"/>.
    /// Out-of-range indices are ignored.
    /// </summary>
    public void Show(int index, StatusEffectDefinition definition, int stacks)
    {
        StatusIconView view = Icon(index);
        if (view != null)
        {
            view.Show(definition, stacks);
        }
    }

    /// <summary>
    /// Sets the elapsed fill of icon <paramref name="index"/>, 0 to 1. Out-of-range indices are ignored.
    /// </summary>
    public void SetElapsed(int index, float fraction)
    {
        StatusIconView view = Icon(index);
        if (view != null)
        {
            view.SetElapsed(fraction);
        }
    }

    /// <summary>
    /// Hides every icon from <paramref name="index"/> on.
    /// </summary>
    public void HideFrom(int index)
    {
        for (int i = Mathf.Max(0, index); i < Capacity; i++)
        {
            if (icons[i] != null)
            {
                icons[i].Hide();
            }
        }
    }

    /// <summary>
    /// Hides every icon, for when there is no local player.
    /// </summary>
    public void Clear()
    {
        HideFrom(0);
    }

    /// <summary>
    /// The icon view at <paramref name="index"/>, or null when the index is out of range or none is assigned.
    /// </summary>
    internal StatusIconView Icon(int index)
    {
        return icons != null && index >= 0 && index < icons.Length ? icons[index] : null;
    }

    /// <summary>
    /// Assigns the icon views in code, for tests that build the bar without a prefab.
    /// </summary>
    internal void ConfigureReferences(StatusIconView[] iconViews)
    {
        icons = iconViews;
    }
}
