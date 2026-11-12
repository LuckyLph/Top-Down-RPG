using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tests for the Saved games screen (<see cref="SavedGamesController"/>, Docs/UiRevamp.md, Phase 10) on the real prefab,
/// over a fake game flow and network connection: the cards newest first with their places, a copy's tag and their times;
/// the focus along the cards and across their actions, only the selected card showing its actions; Play and Host
/// starting the card's own save; Delete asking first, Cancel keeping the save and confirming deleting it and moving the
/// focus; deleting the last save closing the screen; and the screen opened to host.
/// </summary>
public class SavedGamesControllerTests
{
    private const string ScreenPath = "Assets/Prefabs/UI/SavedGames.prefab";

    private MenuTestRig rig;
    private SavedGamesView view;
    private SavedGamesController controller;
    private int closes;

    /// <summary>
    /// Builds the rig and a controller over an instance of the screen in a 640 x 360 UI px parent.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        rig = new MenuTestRig();
        GameObject parent = rig.Track(new GameObject("Canvas", typeof(RectTransform)));
        ((RectTransform)parent.transform).sizeDelta = new Vector2(640f, 360f);
        GameObject screen = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ScreenPath), parent.transform);
        view = screen.GetComponent<SavedGamesView>();
        controller = rig.CreateSavedGames(view);
        closes = 0;
    }

    /// <summary>
    /// Disposes the controller and the rig.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        controller.Dispose();
        rig.Dispose();
    }

    /// <summary>
    /// Opening lists every save newest first, sessions and copies alike: the place (the nearest entry's name), "from a
    /// host's game" after a copy, and the time played with when it was saved; it selects the newest card's Play, shows
    /// that card's actions only, and pushes a menu layer. Without a save it does not open.
    /// </summary>
    [Test]
    public void Opening_ListsEverySave_NewestFirst_AndSelectsTheNewestsPlay()
    {
        controller.Open(Closed, false);
        Assert.That(controller.IsOpen, Is.False, "Nothing to list.");

        string oldest = rig.WriteSave(new Vector2(12f, 14f), 25d * 60d, "oldest");
        rig.WallClock.Advance(3600d);
        string copy = rig.WriteCopy(MenuTestRig.NearNorth, 3900d, "host-session", false);
        rig.WallClock.Advance(3600d);
        string newest = rig.WriteSave(MenuTestRig.NearNorth, 3d * 3600d + 12d * 60d, "newest");

        Open();

        Assert.That(view.IsShown, Is.True);
        Assert.That(view.Title.text, Is.EqualTo(SavedGamesController.Title));
        Assert.That(view.CardCount, Is.EqualTo(3));
        Assert.That(view.Card(0).PlaceText.text, Is.EqualTo("North road"));
        Assert.That(view.Card(0).TagText.gameObject.activeSelf, Is.False);
        Assert.That(view.Card(1).PlaceText.text, Is.EqualTo("North road"));
        Assert.That(view.Card(1).TagText.gameObject.activeSelf, Is.True);
        Assert.That(view.Card(1).TagText.text, Is.EqualTo(ContinueLine.Separator + SavedGamesController.CopyTag));
        Assert.That(view.Card(2).PlaceText.text, Is.EqualTo("Start camp"));
        Assert.That(rig.Save.TryLoad(newest, out GameSaveData data), Is.True);
        Assert.That(view.Card(0).DetailsText.text, Is.EqualTo(rig.Line.DetailsOf(data)));
        Assert.That(view.Card(0).DetailsText.text, Does.StartWith("3 h 12 min · "));
        Assert.That(view.Card(2).DetailsText.text, Does.StartWith("25 min · "));
        Assert.That(rig.Selection.Current, Is.EqualTo(view.Card(0).PlayButton.gameObject));
        Assert.That(Shown(0), Is.True);
        Assert.That(Shown(1) || Shown(2), Is.False, "Only the selected card shows its actions.");
        Assert.That(rig.Menus.Top, Is.SameAs(controller));
        Assert.That(oldest, Is.Not.EqualTo(copy));
    }

    /// <summary>
    /// Up and Down move to the same action of the card above or below, through Back past either end; Left and Right
    /// move along the card's actions; moving onto another card shows its actions and hides the last one's; Back returns
    /// to the last card, or with Down to the first, at the action used last.
    /// </summary>
    [Test]
    public void TheFocus_MovesAlongTheCards_AndAcrossTheirActions_ThroughBack()
    {
        WriteThree();
        Open();
        SessionCardView first = view.Card(0);
        SessionCardView second = view.Card(1);
        SessionCardView last = view.Card(2);

        Assert.That(first.PlayButton.navigation.selectOnDown, Is.EqualTo(second.PlayButton));
        Assert.That(first.PlayButton.navigation.selectOnUp, Is.EqualTo(view.BackButton));
        Assert.That(first.PlayButton.navigation.selectOnRight, Is.EqualTo(first.HostButton));
        Assert.That(first.PlayButton.navigation.selectOnLeft, Is.Null);
        Assert.That(first.HostButton.navigation.selectOnRight, Is.EqualTo(first.DeleteButton));
        Assert.That(first.DeleteButton.navigation.selectOnRight, Is.Null);
        Assert.That(second.HostButton.navigation.selectOnUp, Is.EqualTo(first.HostButton));
        Assert.That(last.DeleteButton.navigation.selectOnDown, Is.EqualTo(view.BackButton));
        Assert.That(view.BackButton.navigation.selectOnUp, Is.EqualTo(last.PlayButton));
        Assert.That(view.BackButton.navigation.selectOnDown, Is.EqualTo(first.PlayButton));

        Move(second.HostButton);

        Assert.That(controller.SelectedCard, Is.EqualTo(1));
        Assert.That(Shown(1), Is.True);
        Assert.That(Shown(0), Is.False, "The card left hides its actions.");
        Assert.That(second.SelectedFrame.enabled && !first.SelectedFrame.enabled, Is.True);
        Assert.That(view.BackButton.navigation.selectOnUp, Is.EqualTo(last.HostButton), "Back returns at the action used last.");
        Assert.That(view.BackButton.navigation.selectOnDown, Is.EqualTo(first.HostButton));
    }

    /// <summary>
    /// A card's Play plays its own save offline and its Host hosts it, whatever the latest is.
    /// </summary>
    [Test]
    public void PlayAndHost_StartTheCardsOwnSave()
    {
        WriteThree();
        Open();

        view.Card(2).PlayButton.onClick.Invoke();
        Assert.That(rig.SessionSave.Id, Is.EqualTo("oldest"));
        Assert.That(rig.Flow.StartedPosition, Is.EqualTo(rig.Home.ToWorld(new Vector2(12f, 14f))));
        Assert.That(rig.Connection.HostsStarted, Is.Zero);

        view.Card(1).HostButton.onClick.Invoke();
        Assert.That(rig.Connection.HostsStarted, Is.EqualTo(1));
        Assert.That(rig.SessionSave.Id, Is.EqualTo("middle"));
        Assert.That(view.Notice.text, Is.Empty);
    }

    /// <summary>
    /// A start that cannot go ahead says why on the notice line.
    /// </summary>
    [Test]
    public void AHostThatFails_SaysWhyOnTheNoticeLine()
    {
        WriteThree();
        Open();
        rig.Connection.HostStarts = false;

        UnityEngine.TestTools.LogAssert.Expect(LogType.Error, "Could not start hosting on port 7777.");
        view.Card(0).HostButton.onClick.Invoke();

        Assert.That(view.Notice.text, Is.EqualTo(DialogNoticeView.BySentence(string.Format(MenuSessionStarter.HostFailedFormat, 7777))));
    }

    /// <summary>
    /// Delete asks first, naming the save, Cancel selected; Cancel keeps it and selects its Delete again; Back closes the
    /// question too; confirming deletes it, keeping its copy, and selects the Delete of the card that takes its place.
    /// </summary>
    [Test]
    public void Delete_AsksFirst_CancelKeepsIt_AndConfirmingDeletesIt_MovingTheFocus()
    {
        WriteThree();
        Open();
        SessionCardView second = view.Card(1);
        Move(second.DeleteButton);

        second.DeleteButton.onClick.Invoke();
        Assert.That(view.IsConfirmShown, Is.True);
        Assert.That(view.ConfirmText.text, Is.EqualTo(SavedGamesController.DeleteQuestion + "\nNorth road" + ContinueLine.Separator + second.DetailsText.text));
        Assert.That(rig.Selection.Current, Is.EqualTo(view.ConfirmNoButton.gameObject));

        view.ConfirmNoButton.onClick.Invoke();
        Assert.That(view.IsConfirmShown, Is.False);
        Assert.That(rig.Folder.Names(), Has.Count.EqualTo(3), "Cancel keeps it.");
        Assert.That(rig.Selection.Current, Is.EqualTo(second.DeleteButton.gameObject));

        second.DeleteButton.onClick.Invoke();
        controller.HandleBack();
        Assert.That(view.IsConfirmShown, Is.False, "Back closes the question.");
        Assert.That(controller.IsOpen, Is.True);

        second.DeleteButton.onClick.Invoke();
        view.ConfirmYesButton.onClick.Invoke();
        string middle = GameSave.SessionPrefix + "middle" + FileSaveFolder.Extension;
        Assert.That(rig.Folder.Names(), Does.Not.Contain(middle));
        Assert.That(rig.Folder.DeletedCopyOf(middle), Is.Not.Null, "A copy is kept.");
        Assert.That(view.CardCount, Is.EqualTo(2));
        Assert.That(view.Card(1).PlaceText.text, Is.EqualTo("Start camp"), "The older save takes its place.");
        Assert.That(rig.Selection.Current, Is.EqualTo(view.Card(1).DeleteButton.gameObject));
        Assert.That(controller.SelectedCard, Is.EqualTo(1));
    }

    /// <summary>
    /// Deleting a joined client's copy asks about its place in a host's game; deleting the last card moves the focus to
    /// the card above it.
    /// </summary>
    [Test]
    public void DeletingTheLastCard_MovesTheFocusUp_AndACopyIsAskedAboutAsItsPlace()
    {
        rig.WriteSave(MenuTestRig.NearNorth, 600d, "newest");
        rig.WallClock.Advance(-60d);
        rig.WriteCopy(new Vector2(12f, 14f), 300d, "host-session", true);
        Open();
        SessionCardView copy = view.Card(1);
        Move(copy.DeleteButton);

        copy.DeleteButton.onClick.Invoke();
        Assert.That(view.ConfirmText.text, Does.StartWith(SavedGamesController.DeleteCopyQuestion + "\nStart camp"));
        view.ConfirmYesButton.onClick.Invoke();

        Assert.That(view.CardCount, Is.EqualTo(1));
        Assert.That(rig.Selection.Current, Is.EqualTo(view.Card(0).DeleteButton.gameObject));
    }

    /// <summary>
    /// Deleting the last save closes the screen, its callback told.
    /// </summary>
    [Test]
    public void DeletingTheLastSave_ClosesTheScreen()
    {
        rig.WriteSave(MenuTestRig.NearNorth, 600d);
        Open();

        view.Card(0).DeleteButton.onClick.Invoke();
        view.ConfirmYesButton.onClick.Invoke();

        Assert.That(rig.Folder.Names(), Is.Empty);
        Assert.That(controller.IsOpen, Is.False);
        Assert.That(view.IsShown, Is.False);
        Assert.That(closes, Is.EqualTo(1));
        Assert.That(rig.Menus.Top, Is.Null);
    }

    /// <summary>
    /// Opened to host, the screen is titled "Host a saved game", its cards hold Host and Delete without Play, it selects
    /// the newest card's Host, and Host hosts the card's save.
    /// </summary>
    [Test]
    public void OpenedToHost_ItsCardsHostAndDelete_WithoutPlay()
    {
        WriteThree();
        controller.Open(Closed, true);

        Assert.That(view.Title.text, Is.EqualTo(SavedGamesController.HostingTitle));
        SessionCardView first = view.Card(0);
        Assert.That(first.PlayButton.gameObject.activeSelf, Is.False);
        Assert.That(first.ActionCount, Is.EqualTo(2));
        Assert.That(rig.Selection.Current, Is.EqualTo(first.HostButton.gameObject));
        Assert.That(first.HostButton.navigation.selectOnLeft, Is.Null);
        Assert.That(first.HostButton.navigation.selectOnRight, Is.EqualTo(first.DeleteButton));
        Assert.That(first.HostButton.navigation.selectOnDown, Is.EqualTo(view.Card(1).HostButton));

        view.Card(2).HostButton.onClick.Invoke();
        Assert.That(rig.Connection.HostsStarted, Is.EqualTo(1));
        Assert.That(rig.SessionSave.Id, Is.EqualTo("oldest"));

        controller.Close();
        controller.Open(Closed, false);
        Assert.That(view.Card(0).PlayButton.gameObject.activeSelf, Is.True, "Opened from the main menu, Play is back.");
        Assert.That(view.Title.text, Is.EqualTo(SavedGamesController.Title));
    }

    /// <summary>
    /// A click on a card selects its first action, which shows its actions.
    /// </summary>
    [Test]
    public void AClickOnACard_SelectsItsFirstAction()
    {
        WriteThree();
        Open();

        view.Card(2).OnPointerClick(new UnityEngine.EventSystems.PointerEventData(null) { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left });

        Assert.That(rig.Selection.Current, Is.EqualTo(view.Card(2).PlayButton.gameObject));
        Assert.That(Shown(2) && !Shown(0), Is.True);
    }

    /// <summary>
    /// Writes three sessions an hour apart, newest first: "newest" and "middle" at the north road, "oldest" at the start
    /// camp.
    /// </summary>
    private void WriteThree()
    {
        rig.WriteSave(new Vector2(12f, 14f), 25d * 60d, "oldest");
        rig.WallClock.Advance(3600d);
        rig.WriteSave(MenuTestRig.NearNorth, 3900d, "middle");
        rig.WallClock.Advance(3600d);
        rig.WriteSave(MenuTestRig.NearNorth, 600d, "newest");
    }

    /// <summary>
    /// Opens the screen from the main menu.
    /// </summary>
    private void Open()
    {
        controller.Open(Closed, false);
    }

    /// <summary>
    /// Selects <paramref name="target"/> as the arrows would, then lets the screen follow it.
    /// </summary>
    private void Move(Selectable target)
    {
        rig.Selection.Select(target);
        controller.LateTick();
    }

    /// <summary>
    /// Whether the card at <paramref name="index"/> shows its actions to sight and the pointer.
    /// </summary>
    private bool Shown(int index)
    {
        CanvasGroup actions = view.Card(index).Actions;
        return actions.alpha > 0f && actions.blocksRaycasts;
    }

    /// <summary>
    /// Counts the screen's closings.
    /// </summary>
    private void Closed()
    {
        closes++;
    }
}
