using UnityEngine;

public readonly struct DamageReport
{
    public DamageReport(Health target, int amount, DamageType type, DamageFlags flags, GameObject source, Vector3 popupWorldPosition)
    {
        Target = target;
        Amount = amount;
        Type = type;
        Flags = flags;
        Source = source;
        PopupWorldPosition = popupWorldPosition;
    }

    public Health Target { get; }
    public int Amount { get; }
    public DamageType Type { get; }
    public DamageFlags Flags { get; }
    public GameObject Source { get; }
    public Vector3 PopupWorldPosition { get; }
    public bool IsImmune => (Flags & DamageFlags.Immune) != 0;
}
