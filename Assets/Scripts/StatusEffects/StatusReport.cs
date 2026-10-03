using UnityEngine;

/// <summary>
/// A status that was blocked by an immunity: target, status, source and where its popup starts.
/// </summary>
public readonly struct StatusReport
{
    public StatusReport(Health target, StatusEffectDefinition definition, GameObject source, Vector3 popupWorldPosition)
    {
        Target = target;
        Definition = definition;
        Source = source;
        PopupWorldPosition = popupWorldPosition;
    }

    public Health Target { get; }
    public StatusEffectDefinition Definition { get; }
    public GameObject Source { get; }
    public Vector3 PopupWorldPosition { get; }
}
