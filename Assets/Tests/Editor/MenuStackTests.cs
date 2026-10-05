using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Tests for <see cref="MenuStack"/>: routing Back and the menu key to the top menu layer, requesting a menu when
/// none is open, and keeping the stack's order.
/// </summary>
public class MenuStackTests
{
    private FakeMenuInput input;
    private MenuStack menus;
    private int requests;

    /// <summary>
    /// Builds a menu stack on fake menu input that counts its menu requests.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        input = new FakeMenuInput();
        menus = new MenuStack(input);
        menus.MenuRequested += () => requests++;
        requests = 0;
    }

    /// <summary>
    /// With no menu open, Back does nothing and the menu key raises one menu request.
    /// </summary>
    [Test]
    public void TheMenuKey_WithNoMenuOpen_RequestsOne_AndBackDoesNothing()
    {
        Tick(back: true);
        Assert.That(requests, Is.Zero);

        Tick(menu: true);
        Assert.That(requests, Is.EqualTo(1));
    }

    /// <summary>
    /// Back or the menu key is handled only by the top layer, once per tick even when both are pressed, and the
    /// menu key requests no new menu while one is open.
    /// </summary>
    [Test]
    public void BackOrTheMenuKey_GoesOnlyToTheTopMenu_OncePerTick()
    {
        List<string> handled = new();
        FakeLayer bottom = new("bottom", handled);
        FakeLayer top = new("top", handled);
        menus.Push(bottom);
        menus.Push(top);

        Tick(menu: true, back: true);
        Tick(menu: true);

        Assert.That(handled, Is.EqualTo(new[] { "top", "top" }));
        Assert.That(requests, Is.Zero, "The menu key goes to the open menu instead of requesting one.");
    }

    /// <summary>
    /// A top layer that removes itself while handling Back does not pass that Back on; the layer below handles
    /// only the next tick's Back.
    /// </summary>
    [Test]
    public void ALayerThatClosesWhileHandlingBack_LeavesTheNextOneForTheNextTick()
    {
        List<string> handled = new();
        FakeLayer bottom = new("bottom", handled);
        FakeLayer top = new("top", handled) { RemoveOnBack = menus };
        menus.Push(bottom);
        menus.Push(top);

        Tick(back: true);
        Assert.That(handled, Is.EqualTo(new[] { "top" }));
        Assert.That(menus.Top, Is.SameAs(bottom));

        Tick(back: true);
        Assert.That(handled, Is.EqualTo(new[] { "top", "bottom" }));
    }

    /// <summary>
    /// Pushing a layer already on the stack or a null layer adds nothing, and removing a layer from the middle
    /// keeps the others in order.
    /// </summary>
    [Test]
    public void PushingTwice_AddsOnce_AndRemovingFromTheMiddleKeepsTheOrder()
    {
        FakeLayer a = new("a", new List<string>());
        FakeLayer b = new("b", new List<string>());
        FakeLayer c = new("c", new List<string>());
        menus.Push(a);
        menus.Push(b);
        menus.Push(b);
        menus.Push(c);
        menus.Push(null);
        Assert.That(menus.Count, Is.EqualTo(3));

        menus.Remove(b);
        Assert.That(menus.Top, Is.SameAs(c));

        menus.Remove(c);
        Assert.That(menus.Top, Is.SameAs(a));

        menus.Remove(a);
        Assert.That(menus.Top, Is.Null);
    }

    /// <summary>
    /// Sets this frame's menu and back presses and ticks the menu stack once.
    /// </summary>
    private void Tick(bool menu = false, bool back = false)
    {
        input.MenuPressedThisFrame = menu;
        input.BackPressedThisFrame = back;
        menus.Tick();
    }

    /// <summary>
    /// Menu layer that logs its name each time it handles Back and can remove itself from a stack when it does.
    /// </summary>
    private sealed class FakeLayer : IMenuLayer
    {
        private readonly string name;
        private readonly List<string> handled;

        /// <summary>
        /// Creates a layer that appends <paramref name="name"/> to the shared <paramref name="handled"/> log.
        /// </summary>
        public FakeLayer(string name, List<string> handled)
        {
            this.name = name;
            this.handled = handled;
        }

        public MenuStack RemoveOnBack { get; set; }

        /// <summary>
        /// Logs this layer's name and, when <see cref="RemoveOnBack"/> is set, removes itself from that stack.
        /// </summary>
        public void HandleBack()
        {
            handled.Add(name);
            if (RemoveOnBack != null)
            {
                RemoveOnBack.Remove(this);
            }
        }
    }
}
