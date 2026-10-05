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

    /// <summary>
    /// Creates an empty stack reading the menu keys from <paramref name="menuInput"/>.
    /// </summary>
    public MenuStack(IMenuInput menuInput)
    {
        this.menuInput = menuInput;
    }

    public event Action MenuRequested;

    public int Count => layers.Count;
    public IMenuLayer Top => layers.Count > 0 ? layers[layers.Count - 1] : null;

    /// <summary>
    /// Puts <paramref name="layer"/> on top; ignores null and a layer already on the stack, which keeps its place.
    /// </summary>
    public void Push(IMenuLayer layer)
    {
        if (layer != null && !layers.Contains(layer))
        {
            layers.Add(layer);
        }
    }

    /// <summary>
    /// Takes <paramref name="layer"/> off the stack wherever it is; does nothing when it is not on it.
    /// </summary>
    public void Remove(IMenuLayer layer)
    {
        layers.Remove(layer);
    }

    /// <summary>
    /// Sends this frame's Back or Menu press to the top menu, or raises <see cref="MenuRequested"/> for a Menu press
    /// with no menu open; a lone Back press with no menu open does nothing.
    /// </summary>
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
