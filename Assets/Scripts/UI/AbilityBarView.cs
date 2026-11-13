using TMPro;
using UnityEngine;

/// <summary>
/// Passive ability bar on the player HUD: six <see cref="AbilitySlotView"/>s, each labelled with its bound key, and the
/// message line above them, where a failed press says why (Docs/UiRevamp.md, Decisions: Failures).
/// </summary>
[DisallowMultipleComponent]
public class AbilityBarView : MonoBehaviour
{
    [SerializeField] private AbilitySlotView[] slots = new AbilitySlotView[AbilitySlots.Count];
    [SerializeField, Tooltip("The message line above the bar, a MessageLine, hidden until a failed press shows its reason.")]
    private TMP_Text messageText;

    public int SlotCount => slots != null ? slots.Length : 0;
    internal TMP_Text MessageText => messageText;
    internal bool IsMessageShown => messageText != null && messageText.gameObject.activeSelf;

    /// <summary>
    /// Logs an error unless exactly one slot view per ability slot and the message line are assigned.
    /// </summary>
    private void Awake()
    {
        if (slots == null || slots.Length != AbilitySlots.Count)
        {
            Debug.LogError($"{name} needs exactly {AbilitySlots.Count} ability slot views.", this);
        }

        if (messageText == null)
        {
            Debug.LogError($"{name} has no message line.", this);
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
    /// Shows whether <paramref name="slot"/>'s cast runs. Out-of-range slots are ignored.
    /// </summary>
    public void SetCasting(int slot, bool casting)
    {
        AbilitySlotView view = Slot(slot);
        if (view != null)
        {
            view.SetCasting(casting);
        }
    }

    /// <summary>
    /// Shows whether <paramref name="slot"/>'s cast waits in the buffer. Out-of-range slots are ignored.
    /// </summary>
    public void SetQueued(int slot, bool waiting)
    {
        AbilitySlotView view = Slot(slot);
        if (view != null)
        {
            view.SetQueued(waiting);
        }
    }

    /// <summary>
    /// Shows whether crowd control blocks <paramref name="slot"/>'s ability. Out-of-range slots are ignored.
    /// </summary>
    public void SetDisabled(int slot, bool disabled)
    {
        AbilitySlotView view = Slot(slot);
        if (view != null)
        {
            view.SetDisabled(disabled);
        }
    }

    /// <summary>
    /// Shows <paramref name="message"/> on the message line.
    /// </summary>
    public void ShowMessage(string message)
    {
        if (messageText == null)
        {
            return;
        }

        messageText.text = message;
        messageText.gameObject.SetActive(true);
    }

    /// <summary>
    /// Hides the message line.
    /// </summary>
    public void HideMessage()
    {
        if (messageText != null)
        {
            messageText.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Empties every slot and hides the message line, for when there is no local player.
    /// </summary>
    public void Clear()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            SetAbility(i, null);
        }

        HideMessage();
    }

    /// <summary>
    /// Assigns the slot views and the message line in code, for tests that build the bar without a prefab.
    /// </summary>
    internal void ConfigureReferences(AbilitySlotView[] slotViews, TMP_Text message = null)
    {
        slots = slotViews;
        messageText = message;
    }

    /// <summary>
    /// The view for <paramref name="slot"/>, or null when the index is out of range or none is assigned.
    /// </summary>
    private AbilitySlotView Slot(int slot)
    {
        return slots != null && slot >= 0 && slot < slots.Length ? slots[slot] : null;
    }
}
