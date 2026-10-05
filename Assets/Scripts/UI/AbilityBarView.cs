using UnityEngine;

/// <summary>
/// Passive ability bar on the player HUD: six <see cref="AbilitySlotView"/>s, each labelled with its bound key.
/// </summary>
[DisallowMultipleComponent]
public class AbilityBarView : MonoBehaviour
{
    [SerializeField] private AbilitySlotView[] slots = new AbilitySlotView[AbilitySlots.Count];

    public int SlotCount => slots != null ? slots.Length : 0;

    /// <summary>
    /// Logs an error unless exactly one slot view per ability slot is assigned.
    /// </summary>
    private void Awake()
    {
        if (slots == null || slots.Length != AbilitySlots.Count)
        {
            Debug.LogError($"{name} needs exactly {AbilitySlots.Count} ability slot views.", this);
        }
    }

    /// <summary>
    /// Shows <paramref name="ability"/> in <paramref name="slot"/> (empty when null). Out-of-range slots are
    /// ignored.
    /// </summary>
    public void SetAbility(int slot, AbilityDefinition ability)
    {
        AbilitySlotView view = Slot(slot);
        if (view != null)
        {
            view.SetAbility(ability);
        }
    }

    /// <summary>
    /// Shows the remaining cooldown <paramref name="fraction"/> and whole <paramref name="seconds"/> on
    /// <paramref name="slot"/>. Out-of-range slots are ignored.
    /// </summary>
    public void SetCooldown(int slot, float fraction, int seconds)
    {
        AbilitySlotView view = Slot(slot);
        if (view != null)
        {
            view.SetCooldown(fraction, seconds);
        }
    }

    /// <summary>
    /// Sets the bound key label shown on <paramref name="slot"/>. Out-of-range slots are ignored.
    /// </summary>
    public void SetKey(int slot, string key)
    {
        AbilitySlotView view = Slot(slot);
        if (view != null)
        {
            view.SetKey(key);
        }
    }

    /// <summary>
    /// Sets the strength of <paramref name="slot"/>'s failure flash, 0 to 1. Out-of-range slots are ignored.
    /// </summary>
    public void SetFlash(int slot, float alpha)
    {
        AbilitySlotView view = Slot(slot);
        if (view != null)
        {
            view.SetFlash(alpha);
        }
    }

    /// <summary>
    /// Empties every slot, for when there is no local player.
    /// </summary>
    public void Clear()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            SetAbility(i, null);
        }
    }

    /// <summary>
    /// Assigns the slot views in code, for tests that build the bar without a prefab.
    /// </summary>
    internal void ConfigureReferences(AbilitySlotView[] slotViews)
    {
        slots = slotViews;
    }

    /// <summary>
    /// The view for <paramref name="slot"/>, or null when the index is out of range or none is assigned.
    /// </summary>
    private AbilitySlotView Slot(int slot)
    {
        return slots != null && slot >= 0 && slot < slots.Length ? slots[slot] : null;
    }
}
