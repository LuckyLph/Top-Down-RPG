using System;
using System.Collections.Generic;
using VContainer.Unity;

/// <summary>
/// The open menus, newest on top, and the only reader of the menu keys. Each tick it handles at most one press:
/// Back or the Menu key goes to the top menu, and the Menu key with no menu open raises <see cref="MenuRequested"/>.
/// Escape is both keys, so it still counts once. Menus push themselves when they open and remove themselves when they
/// close or are disposed.
/// </summary>
public sealed class MenuStack : ITickable
{
    private readonly IMenuInput menuInput;
    private readonly List<IMenuLayer> layers = new();

    public MenuStack(IMenuInput menuInput)
    {
        this.menuInput = menuInput;
    }

    public event Action MenuRequested;

    public int Count => layers.Count;
    public IMenuLayer Top => layers.Count > 0 ? layers[layers.Count - 1] : null;

    public void Push(IMenuLayer layer)
    {
        if (layer != null && !layers.Contains(layer))
        {
            layers.Add(layer);
        }
    }

    public void Remove(IMenuLayer layer)
    {
        layers.Remove(layer);
    }

    public void Tick()
    {
        bool menu = menuInput.MenuPressedThisFrame;
        if (!menu && !menuInput.BackPressedThisFrame)
        {
            return;
        }

        if (layers.Count > 0)
        {
            layers[layers.Count - 1].HandleBack();
        }
        else if (menu)
        {
            MenuRequested?.Invoke();
        }
    }
}
