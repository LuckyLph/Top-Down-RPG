using System.Collections.Generic;
using NUnit.Framework;

public class MenuStackTests
{
    private FakeMenuInput input;
    private MenuStack menus;
    private int requests;

    [SetUp]
    public void SetUp()
    {
        input = new FakeMenuInput();
        menus = new MenuStack(input);
        menus.MenuRequested += () => requests++;
        requests = 0;
    }

    [Test]
    public void TheMenuKey_WithNoMenuOpen_RequestsOne_AndBackDoesNothing()
    {
        Tick(back: true);
        Assert.That(requests, Is.Zero);

        Tick(menu: true);
        Assert.That(requests, Is.EqualTo(1));
    }

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

    private void Tick(bool menu = false, bool back = false)
    {
        input.MenuPressedThisFrame = menu;
        input.BackPressedThisFrame = back;
        menus.Tick();
    }

    private sealed class FakeLayer : IMenuLayer
    {
        private readonly string name;
        private readonly List<string> handled;

        public FakeLayer(string name, List<string> handled)
        {
            this.name = name;
            this.handled = handled;
        }

        public MenuStack RemoveOnBack { get; set; }

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
