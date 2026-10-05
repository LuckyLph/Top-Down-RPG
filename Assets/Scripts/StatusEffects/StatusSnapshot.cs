/// <summary>
/// One active status instance as replicated and shown: its definition, instance id, stacks and remaining seconds.
/// </summary>
public readonly struct StatusSnapshot
{
    /// <summary>
    /// Captures one instance's definition, id, stack count and remaining seconds.
    /// </summary>
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
