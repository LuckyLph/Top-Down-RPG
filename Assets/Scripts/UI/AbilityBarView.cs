using UnityEngine;

/// <summary>
/// Passive ability bar on the player HUD: six <see cref="AbilitySlotView"/>s labelled Q W E R | A S.
/// </summary>
[DisallowMultipleComponent]
public class AbilityBarView : MonoBehaviour
{
    [SerializeField] private AbilitySlotView[] slots = new AbilitySlotView[AbilitySlots.Count];

    public int SlotCount => slots != null ? slots.Length : 0;

    private void Awake()
    {
        if (slots == null || slots.Length != AbilitySlots.Count)
        {
            Debug.LogError($"{name} needs exactly {AbilitySlots.Count} ability slot views.", this);
            return;
        }

        LabelKeys();
    }

    public void SetAbility(int slot, AbilityDefinition ability)
    {
        AbilitySlotView view = Slot(slot);
        if (view != null)
        {
            view.SetAbility(ability);
        }
    }

    public void SetCooldown(int slot, float fraction, int seconds)
    {
        AbilitySlotView view = Slot(slot);
        if (view != null)
        {
            view.SetCooldown(fraction, seconds);
        }
    }

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

    internal void ConfigureReferences(AbilitySlotView[] slotViews)
    {
        slots = slotViews;
        LabelKeys();
    }

    private AbilitySlotView Slot(int slot)
    {
        return slots != null && slot >= 0 && slot < slots.Length ? slots[slot] : null;
    }

    private void LabelKeys()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
            {
                slots[i].SetKey(AbilitySlots.KeyName(i));
            }
        }
    }
}
