/// <summary>
/// One active status instance as replicated and shown: its definition, instance id, stacks and remaining seconds.
/// </summary>
public readonly struct StatusSnapshot
{
    public StatusSnapshot(StatusEffectDefinition definition, int instanceId, int stacks, float remaining)
    {
        Definition = definition;
        InstanceId = instanceId;
        Stacks = stacks;
        Remaining = remaining;
    }

    public StatusEffectDefinition Definition { get; }
    public int InstanceId { get; }
    public int Stacks { get; }
    public float Remaining { get; }
}
