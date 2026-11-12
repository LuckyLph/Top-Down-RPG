using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests for the in-game menu online (Docs/UiRevamp.md, Phase 6), with the in-process host and client: the
/// host's Leave asks first, naming the one player connected; a copy of the menu on the client, over its own session and
/// players, leaves at once; and the Party page lists the same players on both machines, each tagging its own "You" and
/// the client tagging the host's.
/// </summary>
public class NetworkGameMenuPlayModeTests
{
    private InProcessClient client;
    private GameObject clientMenu;
    private PartyPagePresenter clientParty;
    private GameMenuController clientController;

    /// <summary>
    /// Disposes the client's menu copy and the in-process client.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        clientController?.Dispose();
        clientController = null;
        clientParty?.Dispose();
        clientParty = null;
        if (clientMenu != null)
        {
            Object.Destroy(clientMenu);
        }

        client?.Dispose();
        client = null;
    }

    /// <summary>
    /// With a client connected, the host's Leave game asks first, naming one other player and saying the game is
    /// saved; Cancel keeps the game and the menu.
    /// </summary>
    [UnityTest]
    public IEnumerator TheHostsLeave_AsksFirst_NamingTheOnePlayerConnected()
    {
        yield return HostAndJoin();
        GameMenuController menu = SceneBootTestHelper.ResolveFromGameplay<GameMenuController>();
        GameMenuView view = Object.FindAnyObjectByType<GameMenuView>();

        menu.Open();
        view.LeaveButton.onClick.Invoke();

        Assert.That(view.IsConfirmShown, Is.True);
        Assert.That(view.ConfirmText.text, Is.EqualTo(GameMenuController.EndGameForOne + "\n" + GameMenuController.GameSaved));
        Assert.That(menu.IsLeaving, Is.False);

        view.ConfirmNoButton.onClick.Invoke();
        Assert.That(view.IsConfirmShown, Is.False);
        Assert.That(menu.IsOpen, Is.True);
        Assert.That(client.Manager.IsConnectedClient, Is.True, "Cancel keeps the game.");
    }

    /// <summary>
    /// A copy of the menu on the client leaves at once, with no question: only its own game ends. Its Party page and the
    /// host's list the same two players in slot order, each machine tagging its own player "You", and the client the
    /// host's "Host".
    /// </summary>
    [UnityTest]
    public IEnumerator AJoinedClient_LeavesAtOnce_AndBothMachinesListTheSameParty()
    {
        yield return HostAndJoin();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        yield return SceneBootTestHelper.WaitUntil(() => client.LocalPlayer.Current.Controller.GetComponent<PlayerSlot>().Number == 2, "the client to read its own slot");

        GameMenuController hostMenu = gameplay.Resolve<GameMenuController>();
        GameMenuView hostView = Object.FindAnyObjectByType<GameMenuView>();
        hostMenu.Open();
        hostView.PartyButton.onClick.Invoke();
        PartyPageView hostPage = hostView.PartyPage;
        Assert.That(gameplay.Resolve<PartyPagePresenter>().ShownCount, Is.EqualTo(2));
        Assert.That(hostPage.Row(0).ShownSlot, Is.EqualTo(1));
        Assert.That(hostPage.Row(0).ShownTag, Is.EqualTo(PartyPagePresenter.You));
        Assert.That(hostPage.Row(1).ShownSlot, Is.EqualTo(2));
        Assert.That(hostPage.Row(1).ShownTag, Is.Empty);
        Assert.That(hostPage.IsInviteShown, Is.True, "The host invites.");

        RecordingExit exit = new();
        BuildClientMenu(gameplay, hostView, exit);
        GameMenuView clientView = clientMenu.GetComponent<GameMenuView>();
        clientController.Open();
        Assert.That(clientView.SaveButton.gameObject.activeSelf, Is.False, "The host saves the game.");
        clientView.PartyButton.onClick.Invoke();
        PartyPageView clientPage = clientView.PartyPage;
        Assert.That(clientParty.ShownCount, Is.EqualTo(2));
        Assert.That(clientPage.Row(0).ShownSlot, Is.EqualTo(1));
        Assert.That(clientPage.Row(0).ShownTag, Is.EqualTo(PartyPagePresenter.Host));
        Assert.That(clientPage.Row(1).ShownSlot, Is.EqualTo(2));
        Assert.That(clientPage.Row(1).ShownTag, Is.EqualTo(PartyPagePresenter.You));
        Assert.That(clientPage.IsInviteShown, Is.False);

        clientPage.BackButton.onClick.Invoke();
        clientView.LeaveButton.onClick.Invoke();
        Assert.That(clientView.IsConfirmShown, Is.False, "Leaving loses nobody else's game.");
        Assert.That(exit.Leaves, Is.EqualTo(1));
        Assert.That(clientController.IsLeaving, Is.True);
    }

    /// <summary>
    /// Boots into the main menu, hosts a game with every mob off, starts the in-process client, reports it joined with
    /// its world loaded, and waits until both sides see both players.
    /// </summary>
    private IEnumerator HostAndJoin()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }

        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        client = InProcessClient.Start(SceneBootTestHelper.ResolveFromMain<NetworkSession>(), gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(
            () => hostPlayers.Players.Count == 2 && client.Players.Players.Count == 2 && client.LocalPlayer.Current != null,
            "both sides to see both players");
        PlayerSlot hostOnClient = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId).GetComponent<PlayerSlot>();
        yield return SceneBootTestHelper.WaitUntil(() => hostOnClient.Number == 1 && !InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId).GetComponent<PlayerSlot>().IsJoining, "the slots to settle");
    }

    /// <summary>
    /// A copy of the host's menu standing for the client's: its Party page over the client's players and session, its
    /// controller asking the client's session, saving nothing and leaving through <paramref name="exit"/>.
    /// </summary>
    private void BuildClientMenu(IObjectResolver gameplay, GameMenuView hostView, IGameExit exit)
    {
        clientMenu = Object.Instantiate(hostView.gameObject, hostView.transform.parent);
        GameMenuView clientView = clientMenu.GetComponent<GameMenuView>();
        ISessionStatus clientSession = client.Resolve<ISessionStatus>();
        IWallClock wallClock = client.Resolve<IWallClock>();
        HudSettings hud = gameplay.Resolve<HudSettings>();
        clientParty = new PartyPagePresenter(
            clientView.PartyPage,
            client.Players,
            client.LocalPlayer,
            client.Resolve<PlayerWorlds>(),
            client.Resolve<IClock>(),
            clientSession,
            SceneBootTestHelper.ResolveFromMain<HostAddresses>(),
            new SystemClipboard(),
            wallClock,
            hud);
        clientParty.Start();
        clientController = new GameMenuController(
            clientView,
            gameplay.Resolve<OptionsMenuController>(),
            clientParty,
            new MenuStack(new NoMenuInput()),
            SceneBootTestHelper.ResolveFromMain<GameplayInputGate>(),
            SceneBootTestHelper.ResolveFromMain<TimeFreeze>(),
            SceneBootTestHelper.ResolveGameFlow(),
            clientSession,
            new UISelection(null),
            new NoSaving(),
            exit,
            client.Players,
            client.LocalPlayer,
            wallClock,
            hud);
        clientController.Start();
    }

    /// <summary>
    /// <see cref="IGameExit"/> that only counts the ways out taken.
    /// </summary>
    private sealed class RecordingExit : IGameExit
    {
        public int Leaves { get; private set; }

        /// <summary>
        /// Counts a return to the main menu.
        /// </summary>
        public void LeaveToMenu()
        {
            Leaves++;
        }

        /// <summary>
        /// Never expected here.
        /// </summary>
        public void Quit()
        {
            Assert.Fail("The client copy should not quit.");
        }
    }

    /// <summary>
    /// <see cref="IGameSaving"/> of a joined client: it writes no game, and its last copy was written.
    /// </summary>
    private sealed class NoSaving : IGameSaving
    {
        public bool CanSave => false;
        public bool LastWriteFailed => false;

        /// <summary>
        /// Writes nothing.
        /// </summary>
        public bool Save()
        {
            return false;
        }
    }

    /// <summary>
    /// <see cref="IMenuInput"/> that never presses anything, so the copy's menu stack stays out of the host's way.
    /// </summary>
    private sealed class NoMenuInput : IMenuInput
    {
        public bool BackPressedThisFrame => false;
        public bool MenuPressedThisFrame => false;
    }
}
