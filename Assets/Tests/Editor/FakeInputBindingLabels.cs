using System;
using System.Collections.Generic;

public sealed class FakeInputBindingLabels : IInputBindingLabels
{
    private readonly Dictionary<int, string> labels = new();

    public event Action BindingsChanged;

    public string GetAbilityKeyLabel(int slot)
    {
        return labels.TryGetValue(slot, out string label) ? label : string.Empty;
    }

    public void Set(int slot, string label)
    {
        labels[slot] = label;
        BindingsChanged?.Invoke();
    }
}
