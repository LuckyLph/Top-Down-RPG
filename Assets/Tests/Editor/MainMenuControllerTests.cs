using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tests for the main menu (<see cref="MainMenuController"/>, Docs/UiRevamp.md, Phase 9) on a copy of the scene's
/// canvas, over a fake game flow and network connection: Continue and its line with a save, hidden without, the
/// version, the buttons looping, Play together in the panel's place, the host-gone and lost-while-joining dialogs from a
/// <see cref="MenuNotice"/>, a start that cannot go ahead said in the dialog, and the JoinGame entry point.
/// </summary>
public class MainMenuControllerTests
{
    private MenuTestRig rig;
    private MainMenuTestScreen screen;

    /// <summary>
    /// Builds the rig.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        rig = new MenuTestRig();
    }

    /// <summary>
    /// Disposes the screen and the rig.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        screen?.Dispose();
        screen = null;
        rig.Dispose();
    }

    /// <summary>
    /// With a save, Continue shows with its line and is selected, the version shows, and the six buttons loop top to
    /// bottom.
    /// </summary>
    [Test]
    public void WithASave_ContinueAndItsLineShow_AndTheButtonsLoop()
    {
        rig.WriteSave(MenuTestRig.NearNorth, 3d * 3600d + 12d * 60d);
        Open();
        MainMenuController menu = screen.Menu;

        Assert.That(menu.ContinueButton.gameObject.activeSelf && menu.ContinueLineText.gameObject.activeSelf, Is.True);
        Assert.That(rig.Line.TryGet(out string line), Is.True);
        Assert.That(menu.ContinueLineText.text, Is.EqualTo(line));
        Assert.That(line, Does.StartWith("North road · 3 h 12 min · "));
        Assert.That(menu.VersionText.text, Is.EqualTo(Application.version));
        Assert.That(rig.Selection.Current, Is.EqualTo(menu.ContinueButton.gameObject));
        Selectable[] order = { menu.ContinueButton, menu.NewGameButton, menu.PlayTogetherButton, menu.CharacterButton, menu.OptionsButton, Quit() };
        for (int i = 0; i < order.Length; i++)
        {
            Assert.That(order[i].navigation.selectOnDown, Is.EqualTo(order[(i + 1) % order.Length]), $"Down from {order[i].name}");
            Assert.That(order[i].navigation.selectOnUp, Is.EqualTo(order[(i + order.Length - 1) % order.Length]), $"Up from {order[i].name}");
        }
    }

    /// <summary>
    /// Without a save, Continue and its line hide, New game is selected and loops with Quit.
    /// </summary>
    [Test]
    public void WithoutASave_ContinueHides_AndNewGameIsSelected()
    {
        Open();
        MainMenuController menu = screen.Menu;

        Assert.That(menu.ContinueButton.gameObject.activeSelf, Is.False);
        Assert.That(menu.ContinueLineText.gameObject.activeSelf, Is.False);
        Assert.That(rig.Selection.Current, Is.EqualTo(menu.NewGameButton.gameObject));
        Assert.That(menu.NewGameButton.navigation.selectOnUp, Is.EqualTo(Quit()));
        Assert.That(Quit().navigation.selectOnDown, Is.EqualTo(menu.NewGameButton));
        Assert.That(menu.Dialog.IsShown, Is.False);
    }

    /// <summary>
    /// The host gone: a dialog says so with Continue alone and Back, Continue alone selected; it continues the save.
    /// </summary>
    [Test]
    public void TheHostGone_ShowsItsDialog_AndContinueAloneContinues()
    {
        rig.WriteSave(MenuTestRig.NearNorth, 600d);
        MenuNotice notice = new();
        notice.Leave(MenuNoticeKind.HostGone, "100.101.102.103:7777");
        Open(notice);
        DialogNoticeView dialog = screen.Menu.Dialog;

        Assert.That(dialog.IsShown, Is.True);
        Assert.That(dialog.Message, Is.EqualTo("The host left the game.\nContinue carries on alone from where you were, in a saved game of your own."));
        Assert.That(dialog.PrimaryButton.gameObject.activeSelf && dialog.SecondaryButton.gameObject.activeSelf, Is.True);
        Assert.That(rig.Selection.Current, Is.EqualTo(dialog.PrimaryButton.gameObject));
        Assert.That(rig.Menus.Top, Is.SameAs(screen.Menu));

        dialog.PrimaryButton.onClick.Invoke();
        Assert.That(dialog.IsShown, Is.False);
        Assert.That(rig.Flow.WorldStarts, Is.EqualTo(1));
        Assert.That(rig.Flow.StartedPosition, Is.EqualTo(rig.Home.ToWorld(MenuTestRig.NearNorth)));
    }

    /// <summary>
    /// The host gone with no save to continue says only that, with Back; Back (or Escape) closes the dialog and selects
    /// the panel's first button.
    /// </summary>
    [Test]
    public void TheHostGoneWithoutASave_OffersBackAlone_AndBackClosesTheDialog()
    {
        MenuNotice notice = new();
        notice.Leave(MenuNoticeKind.HostGone, "100.101.102.103:7777");
        Open(notice);
        DialogNoticeView dialog = screen.Menu.Dialog;

        Assert.That(dialog.Message, Is.EqualTo(MainMenuController.HostGoneWithoutSaveMessage));
        Assert.That(dialog.PrimaryButton.gameObject.activeSelf, Is.False);
        Assert.That(rig.Selection.Current, Is.EqualTo(dialog.SecondaryButton.gameObject));

        rig.MenuInput.BackPressedThisFrame = true;
        rig.Menus.Tick();
        Assert.That(dialog.IsShown, Is.False);
        Assert.That(rig.Menus.Count, Is.Zero);
        Assert.That(rig.Selection.Current, Is.EqualTo(screen.Menu.NewGameButton.gameObject));
    }

    /// <summary>
    /// Lost while joining: a dialog names the host, and Play together opens the screen with that address and Join
    /// selected, in the panel's place.
    /// </summary>
    [Test]
    public void LostWhileJoining_ShowsItsDialog_AndPlayTogetherReopensTheJoin()
    {
        MenuNotice notice = new();
        notice.Leave(MenuNoticeKind.LostWhileJoining, "100.101.102.103:7777");
        Open(notice);
        DialogNoticeView dialog = screen.Menu.Dialog;

        Assert.That(dialog.Message, Is.EqualTo("Lost the connection while joining 100.101.102.103:7777."));
        dialog.PrimaryButton.onClick.Invoke();

        Assert.That(dialog.IsShown, Is.False);
        Assert.That(screen.PlayTogether.IsOpen, Is.True);
        Assert.That(screen.Menu.Panel.gameObject.activeSelf, Is.False);
        Assert.That(screen.Together.Field.text, Is.EqualTo("100.101.102.103:7777"));
        Assert.That(rig.Selection.Current, Is.EqualTo(screen.Together.JoinButton.gameObject));
    }

    /// <summary>
    /// Play together opens in the panel's place, and closing it brings the panel back with Play together selected.
    /// </summary>
    [Test]
    public void PlayTogether_OpensInThePanelsPlace_AndClosingSelectsItsButton()
    {
        Open();

        screen.Menu.PlayTogetherButton.onClick.Invoke();
        Assert.That(screen.PlayTogether.IsOpen, Is.True);
        Assert.That(screen.Menu.Panel.gameObject.activeSelf, Is.False);

        screen.Together.BackButton.onClick.Invoke();
        Assert.That(screen.Menu.Panel.gameObject.activeSelf, Is.True);
        Assert.That(rig.Selection.Current, Is.EqualTo(screen.Menu.PlayTogetherButton.gameObject));
    }

    /// <summary>
    /// A start that cannot go ahead says why in the dialog, with Back.
    /// </summary>
    [Test]
    public void AStartThatCannotGoAhead_SaysWhyInTheDialog()
    {
        Open();
        rig.Connection.HostStarts = false;
        UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Could not start hosting"));

        screen.Menu.HostGame();

        Assert.That(screen.Menu.Dialog.IsShown, Is.True);
        Assert.That(screen.Menu.Dialog.Message, Is.EqualTo("Could not host on port 7777.\nClose any other copy of the game and try again."));
        Assert.That(screen.Menu.Dialog.PrimaryButton.gameObject.activeSelf, Is.False);
    }

    /// <summary>
    /// JoinGame, the PlayMode tests' entry point, opens Play together and joins the default address, a host on this
    /// machine, and does nothing while a transition runs.
    /// </summary>
    [Test]
    public void JoinGame_JoinsTheDefaultAddressThroughPlayTogether()
    {
        Open();
        rig.Flow.IsTransitioning = true;
        screen.Menu.JoinGame();
        Assert.That(rig.Connection.Joins, Is.Zero);

        rig.Flow.IsTransitioning = false;
        screen.Menu.JoinGame();
        Assert.That(screen.PlayTogether.IsOpen, Is.True);
        Assert.That(rig.Connection.JoinHost, Is.EqualTo("127.0.0.1"));
        Assert.That(rig.Connection.JoinPort, Is.EqualTo(7777));
        Assert.That(screen.PlayTogether.IsConnecting, Is.True);
    }

    /// <summary>
    /// Builds the screen at 640 x 360 with <paramref name="notice"/> and starts it.
    /// </summary>
    private void Open(MenuNotice notice = null)
    {
        screen = new MainMenuTestScreen(rig, 640, 360, notice);
        screen.Start();
    }

    /// <summary>
    /// The Quit button.
    /// </summary>
    private Button Quit()
    {
        return screen.Menu.Panel.Find("QuitButton").GetComponent<Button>();
    }
}
