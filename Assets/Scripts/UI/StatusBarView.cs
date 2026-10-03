using UnityEngine;

/// <summary>
/// Passive row of <see cref="StatusIconView"/>s on the player HUD, above the ability bar.
/// </summary>
[DisallowMultipleComponent]
public class StatusBarView : MonoBehaviour
{
    [SerializeField] private StatusIconView[] icons = new StatusIconView[0];

    public int Capacity => icons != null ? icons.Length : 0;

    private void Awake()
    {
        if (Capacity == 0)
        {
            Debug.LogError($"{name} has no status icon views.", this);
            return;
        }

        Clear();
    }

    public void Show(int index, StatusEffectDefinition definition, int stacks)
    {
        StatusIconView view = Icon(index);
        if (view != null)
        {
            view.Show(definition, stacks);
        }
    }

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

    public void Clear()
    {
        HideFrom(0);
    }

    internal StatusIconView Icon(int index)
    {
        return icons != null && index >= 0 && index < icons.Length ? icons[index] : null;
    }

    internal void ConfigureReferences(StatusIconView[] iconViews)
    {
        icons = iconViews;
    }
}
