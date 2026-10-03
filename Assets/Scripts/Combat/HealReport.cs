using UnityEngine;

/// <summary>
/// A heal that added HP: target, amount added, source and where its popup starts.
/// </summary>
public readonly struct HealReport
{
    public HealReport(Health target, int amount, GameObject source, Vector3 popupWorldPosition)
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
