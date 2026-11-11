using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Passive status groups of the command bar (Docs/UiRevamp.md, Decisions: Statuses): the buffs' <see cref="StatusIconView"/>s
/// to the left of the abilities and the debuffs' to the right, each a 3 x 2 grid whose first icon sits nearest the
/// abilities, filling the bottom row outwards and then the top one.
/// </summary>
[DisallowMultipleComponent]
public class StatusBarView : MonoBehaviour
{
    [SerializeField, FormerlySerializedAs("icons"), Tooltip("The buffs' icons, nearest the abilities first.")]
    private StatusIconView[] buffs = new StatusIconView[0];
    [SerializeField, Tooltip("The debuffs' icons, StatusIconDebuff, nearest the abilities first.")]
    private StatusIconView[] debuffs = new StatusIconView[0];

    public int Capacity => GroupCapacity(StatusKind.Buff) + GroupCapacity(StatusKind.Debuff);

    /// <summary>
    /// Logs an error when either group has no icon views; otherwise hides them all.
    /// </summary>
    private void Awake()
    {
        if (GroupCapacity(StatusKind.Buff) == 0 || GroupCapacity(StatusKind.Debuff) == 0)
        {
            Debug.LogError($"{name} needs status icon views for both buffs and debuffs.", this);
            return;
        }

        Clear();
    }

    /// <summary>
    /// How many icons the group of <paramref name="kind"/> has.
    /// </summary>
    public int GroupCapacity(StatusKind kind)
    {
        StatusIconView[] group = Group(kind);
        return group != null ? group.Length : 0;
    }

    /// <summary>
    /// Shows <paramref name="definition"/> with <paramref name="stacks"/> on icon <paramref name="index"/> of its kind's
    /// group. Out-of-range indices are ignored.
    /// </summary>
    public void Show(StatusKind kind, int index, StatusEffectDefinition definition, int stacks)
    {
        StatusIconView view = Icon(kind, index);
        if (view != null)
        {
            view.Show(definition, stacks);
        }
    }

    /// <summary>
    /// Shows "+<paramref name="count"/>" on icon <paramref name="index"/> of the group of <paramref name="kind"/>, for
    /// the statuses that do not fit, named in <paramref name="names"/>. Out-of-range indices are ignored.
    /// </summary>
    public void ShowMore(StatusKind kind, int index, int count, string names)
    {
        StatusIconView view = Icon(kind, index);
        if (view != null)
        {
            view.ShowMore(count, names);
        }
    }

    /// <summary>
    /// Sets the elapsed fill of icon <paramref name="index"/> of the group of <paramref name="kind"/>, 0 to 1.
    /// Out-of-range indices are ignored.
    /// </summary>
    public void SetElapsed(StatusKind kind, int index, float fraction)
    {
        StatusIconView view = Icon(kind, index);
        if (view != null)
        {
            view.SetElapsed(fraction);
        }
    }

    /// <summary>
    /// Hides every icon of the group of <paramref name="kind"/> from <paramref name="index"/> on.
    /// </summary>
    public void HideFrom(StatusKind kind, int index)
    {
        StatusIconView[] group = Group(kind);
        for (int i = Mathf.Max(0, index); group != null && i < group.Length; i++)
        {
            if (group[i] != null)
            {
                group[i].Hide();
            }
        }
    }

    /// <summary>
    /// Hides every icon, for when there is no local player.
    /// </summary>
    public void Clear()
    {
        HideFrom(StatusKind.Buff, 0);
        HideFrom(StatusKind.Debuff, 0);
    }

    /// <summary>
    /// The icon view at <paramref name="index"/> of the group of <paramref name="kind"/>, or null when the index is out
    /// of range or none is assigned.
    /// </summary>
    internal StatusIconView Icon(StatusKind kind, int index)
    {
        StatusIconView[] group = Group(kind);
        return group != null && index >= 0 && index < group.Length ? group[index] : null;
    }

    /// <summary>
    /// Assigns the two groups' icon views in code, for tests that build the bar without a prefab.
    /// </summary>
    internal void ConfigureReferences(StatusIconView[] buffViews, StatusIconView[] debuffViews)
    {
        buffs = buffViews;
        debuffs = debuffViews;
    }

    /// <summary>
    /// The icons of the group of <paramref name="kind"/>.
    /// </summary>
    private StatusIconView[] Group(StatusKind kind)
    {
        return kind == StatusKind.Buff ? buffs : debuffs;
    }
}
