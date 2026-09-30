using UnityEngine;

// Published by DamageReceiver after damage is applied; presentation (popups, VFX, audio) reacts to it
// so combat outcomes never depend on presentation.
public readonly struct DamageReport
{
    public DamageReport(Health target, int amount, GameObject source, Vector3 popupWorldPosition)
    {
        Target = target;
        Amount = amount;
        Source = source;
        PopupWorldPosition = popupWorldPosition;
    }

    public Health Target { get; }
    public int Amount { get; }
    public GameObject Source { get; }
    public Vector3 PopupWorldPosition { get; }
}
