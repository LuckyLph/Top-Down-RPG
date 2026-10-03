using System;
using System.Collections.Generic;

public sealed class FakeInputBindingLabels : IInputBindingLabels
{
    private readonly Dictionary<string, string> labels = new();

    public event Action BindingsChanged;

    public string GetShortLabel(string actionName)
    {
        return labels.TryGetValue(actionName, out string label) ? label : string.Empty;
    }

    public void Set(string actionName, string label)
    {
        labels[actionName] = label;
        BindingsChanged?.Invoke();
    }
}
