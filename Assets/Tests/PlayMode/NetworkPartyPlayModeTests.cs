using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests for the player slots and the party frames (Docs/UiRevamp.md, Phase 5), with the in-process host and
/// client: the host takes slot 1 and gives a joining client 2, which both machines read the same, the client joining
/// until it reports its world loaded; the host's party frames say the client is joining, then show its bar, and a copy
/// of the frames on the client shows the host; and a client who leaves frees its slot for the next one to join.
/// </summary>
public class NetworkPartyPlayModeTests
{
    private InProcessClient client;
    private PartyFramesPresenter clientPresenter;
    private GameObject clientFrames;

    /// <summary>
    /// Disposes the client's frames and the in-process client.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        clientPresenter?.Dispose();
        clientPresenter = null;
        if (clientFrames != null)
        {
            Object.Destroy(clientFrames);
        }

        client?.Dispose();
        client = null;
    }

    /// <summary>
    /// The host's own player has slot 1. A client joins without reporting its world: its player has slot 2 on both
    /// machines and is joining, which the host's party frame says, and the client reads the host's slot from its spawn.
    /// Once the client reports its world loaded it is joining no more on either machine, the host's frame shows its
    /// bar, and a copy of the frames on the client shows the host, in its slot's colour.
    /// </summary>
    [UnityTest]
    public IEnumerator Slots_AgreeOnBothMachines_AndTheJoiningFlagClearsOnceTheClientIsReady()
    {
        yield return Host();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        PlayerSlot hostOwn = gameplay.Resolve<LocalPlayerTracker>().Current.Controller.GetComponent<PlayerSlot>();
        Assert.That(hostOwn.Number, Is.EqualTo(1), "The host takes the first slot.");
        Assert.That(hostOwn.IsJoining, Is.False);

        client = InProcessClient.Start(SceneBootTestHelper.ResolveFromMain<NetworkSession>(), gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendJoined();
        yield return SceneBootTestHelper.WaitUntil(
            () => hostPlayers.Players.Count == 2 && client.Players.Players.Count == 2 && client.LocalPlayer.Current != null,
            "both sides to see both players");
        PlayerSlot clientOnHost = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId).GetComponent<PlayerSlot>();
        PlayerSlot clientOwn = client.LocalPlayer.Current.Controller.GetComponent<PlayerSlot>();
        PlayerSlot hostOnClient = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId).GetComponent<PlayerSlot>();
        Assert.That(clientOnHost.Number, Is.EqualTo(2), "A joining player gets the lowest free slot.");
        Assert.That(clientOnHost.IsJoining, Is.True, "Joining until its machine reports its world.");
        yield return SceneBootTestHelper.WaitUntil(() => clientOwn.Number == 2, "the client to read its own slot");
        Assert.That(clientOwn.IsJoining, Is.True);
        Assert.That(hostOnClient.Number, Is.EqualTo(1), "The client reads the host's slot from its spawn.");

        PartyFramesPresenter hostPresenter = gameplay.Resolve<PartyFramesPresenter>();
        PartyFramesView hostView = Object.FindAnyObjectByType<PartyFramesView>(FindObjectsInactive.Include);
        yield return null;
        Assert.That(hostPresenter.ShownCount, Is.EqualTo(1), "The host's frames show the client, not the host.");
        Assert.That(hostView.Frame(0).ShownSlot, Is.EqualTo(2));
        Assert.That(hostView.Frame(0).ShownState, Is.EqualTo(PartyFramesPresenter.Joining));

        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(() => !clientOnHost.IsJoining && !clientOwn.IsJoining, "the client's ready report to end its joining on both machines");
        yield return null;
        Assert.That(hostView.Frame(0).ShownState, Is.Null, "The client's frame shows its bar.");
        Assert.That(hostView.Frame(0).ShownFraction, Is.EqualTo(1f));

        clientFrames = Object.Instantiate(hostView.gameObject, hostView.transform.parent);
        PartyFramesView clientView = clientFrames.GetComponent<PartyFramesView>();
        clientPresenter = new PartyFramesPresenter(client.Players, client.LocalPlayer, client.Resolve<PlayerWorlds>(), client.Resolve<IClock>(), gameplay.Resolve<GameplaySettings>(), clientView);
        clientPresenter.Start();
        clientPresenter.Tick();
        Assert.That(clientPresenter.ShownCount, Is.EqualTo(1));
        Assert.That(clientView.Frame(0).ShownSlot, Is.EqualTo(1), "The client's frames show the host.");
        Assert.That(clientView.Frame(0).ShownState, Is.Null);
        UiTheme theme = AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/Data/UI/UiTheme.asset");
        Assert.That(clientView.Frame(0).Badge.color, Is.EqualTo(theme.Colour(UiColour.Player1)), "In the host's slot's colour.");
    }

    /// <summary>
    /// A client joins and takes slot 2, then leaves: the host's party frames empty, and the next client to join takes
    /// slot 2 again and reads the host's 1.
    /// </summary>
    [UnityTest]
    public IEnumerator ALeaversSlot_IsGivenToTheNextOneToJoin()
    {
        yield return Host();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        yield return Join();
        ulong firstId = client.Manager.LocalClientId;
        Assert.That(InProcessClient.FindCopyOwnedBy(hostPlayers, firstId).GetComponent<PlayerSlot>().Number, Is.EqualTo(2));

        client.Dispose();
        client = null;
        yield return SceneBootTestHelper.WaitUntil(() => hostPlayers.Players.Count == 1, "the host to see the client leave");
        yield return null;
        Assert.That(gameplay.Resolve<PartyFramesPresenter>().ShownCount, Is.Zero, "Alone again, no frame shows.");

        yield return Join();
        Assert.That(client.Manager.LocalClientId, Is.Not.EqualTo(firstId), "Another client.");
        Assert.That(InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId).GetComponent<PlayerSlot>().Number, Is.EqualTo(2), "The leaver's slot is reused.");
        PlayerSlot hostOnClient = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId).GetComponent<PlayerSlot>();
        yield return SceneBootTestHelper.WaitUntil(() => client.LocalPlayer.Current.Controller.GetComponent<PlayerSlot>().Number == 2, "the new client to read its slot");
        Assert.That(hostOnClient.Number, Is.EqualTo(1));
    }

    /// <summary>
    /// Boots into the main menu and hosts a game, deactivating every mob.
    /// </summary>
    private static IEnumerator Host()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Starts the in-process client, reports it joined with its world loaded, and waits until both sides see both
    /// players.
    /// </summary>
    private IEnumerator Join()
    {
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        client = InProcessClient.Start(SceneBootTestHelper.ResolveFromMain<NetworkSession>(), gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(
            () => hostPlayers.Players.Count == 2 && client.Players.Players.Count == 2 && client.LocalPlayer.Current != null,
            "both sides to see both players");
    }
}
