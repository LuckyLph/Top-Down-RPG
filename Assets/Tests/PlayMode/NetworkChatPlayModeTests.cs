using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests for the chat (Docs/UiRevamp.md, Phase 12), with the in-process host and client: a client's line
/// reaches the host and the client itself through the host, cleaned and naming the client's slot, and the host's line
/// reaches the client naming slot 1, while a copy of another machine's player sends nothing; and the party's lines show
/// on each machine from what it knows: the host says the client joined once its joining ends and that it is down when it
/// dies, the client says the host is down when the host's player dies, and the host says the client left when it goes.
/// </summary>
public class NetworkChatPlayModeTests
{
    private InProcessClient client;
    private ChatPartyLines clientLines;

    /// <summary>
    /// Disposes the client's party lines and the in-process client.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        clientLines?.Dispose();
        clientLines = null;
        client?.Dispose();
        client = null;
    }

    /// <summary>
    /// The client says a line: the host relays it, cleaned and naming slot 2, to both machines' logs. The host says one:
    /// it reaches both naming slot 1. The client's copy of the host's player sends nothing.
    /// </summary>
    [UnityTest]
    public IEnumerator Lines_ReachEveryMachine_NamingTheSendersSlot()
    {
        yield return Host();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        ChatLog hostLog = gameplay.Resolve<ChatLog>();
        yield return Join(ready: true);
        ChatLog clientLog = client.Resolve<ChatLog>();
        PlayerController clientOwn = client.LocalPlayer.Current.Controller;
        yield return SceneBootTestHelper.WaitUntil(() => clientOwn.GetComponent<PlayerSlot>().Number == 2, "the client to read its slot");
        hostLog.Clear();
        clientLog.Clear();

        Assert.That(clientOwn.GetComponent<PlayerChat>().Say("  on my way一 "), Is.True);
        yield return SceneBootTestHelper.WaitUntil(() => PlayerLines(hostLog).Count == 1 && PlayerLines(clientLog).Count == 1, "the client's line to reach both machines");
        AssertLine(PlayerLines(hostLog)[0], 2, "on my way");
        AssertLine(PlayerLines(clientLog)[0], 2, "on my way");

        Assert.That(gameplay.Resolve<IChatSender>().Send("wait for me"), Is.True, "The host's own line goes through its sender.");
        yield return SceneBootTestHelper.WaitUntil(() => PlayerLines(hostLog).Count == 2 && PlayerLines(clientLog).Count == 2, "the host's line to reach both machines");
        AssertLine(PlayerLines(hostLog)[1], 1, "wait for me");
        AssertLine(PlayerLines(clientLog)[1], 1, "wait for me");

        PlayerChat hostOnClient = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId).GetComponent<PlayerChat>();
        Assert.That(hostOnClient.Say("not mine to say"), Is.False, "A copy of another machine's player sends nothing.");
        Assert.That(clientOwn.GetComponent<PlayerChat>().Say("   "), Is.False, "An empty line goes nowhere.");
        for (int i = 0; i < 10; i++)
        {
            yield return null;
        }

        Assert.That(PlayerLines(hostLog).Count, Is.EqualTo(2));
        Assert.That(PlayerLines(clientLog).Count, Is.EqualTo(2));
    }

    /// <summary>
    /// The client joins: the host says nothing while it is joining, then "Player 2 joined" once its world is loaded, and
    /// "Player 2 is down" when it dies. The host's player dies: the client says "Player 1 is down", the host nothing of
    /// its own. The client leaves: the host says "Player 2 left".
    /// </summary>
    [UnityTest]
    public IEnumerator PartyLines_ShowOnEachMachine()
    {
        yield return Host();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        ChatLog hostLog = gameplay.Resolve<ChatLog>();
        hostLog.Clear();
        yield return Join(ready: false);
        ChatLog clientLog = client.Resolve<ChatLog>();
        clientLines = new ChatPartyLines(client.Players, client.LocalPlayer, clientLog);
        clientLines.Start();
        PlayerController clientOnHost = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId);
        Assert.That(clientOnHost.GetComponent<PlayerSlot>().IsJoining, Is.True);
        Assert.That(PartyLines(hostLog), Is.Empty, "Nothing while the client is joining.");

        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(() => PartyLines(hostLog).Contains("Player 2 joined"), "the host to say the client joined");

        gameplay.Resolve<DamageService>().ApplyDamage(clientOnHost.GetComponent<DamageReceiver>(), clientOnHost.GetComponent<Health>().MaxHealth);
        Assert.That(PartyLines(hostLog), Does.Contain("Player 2 is down"));
        yield return SceneBootTestHelper.WaitUntil(() => client.LocalPlayer.Current.Handle.Health.IsDead, "the client's player to die on the client");
        Assert.That(PartyLines(clientLog), Is.Empty, "The client says nothing of its own death.");

        GameObject hostOwn = gameplay.Resolve<LocalPlayerTracker>().Current.Controller.gameObject;
        gameplay.Resolve<DamageService>().ApplyDamage(hostOwn.GetComponent<DamageReceiver>(), hostOwn.GetComponent<Health>().MaxHealth);
        yield return SceneBootTestHelper.WaitUntil(() => PartyLines(clientLog).Contains("Player 1 is down"), "the client to say the host is down");
        Assert.That(PartyLines(hostLog), Does.Not.Contain("Player 1 is down"), "The host says nothing of its own death.");

        client.Dispose();
        client = null;
        yield return SceneBootTestHelper.WaitUntil(() => PartyLines(hostLog).Contains("Player 2 left"), "the host to say the client left");
    }

    /// <summary>
    /// Asserts <paramref name="line"/> is <paramref name="text"/> from slot <paramref name="slot"/>.
    /// </summary>
    private static void AssertLine(ChatLine line, int slot, string text)
    {
        Assert.That(line.Slot, Is.EqualTo(slot));
        Assert.That(line.Text, Is.EqualTo(text));
    }

    /// <summary>
    /// The players' lines of <paramref name="log"/>, oldest first.
    /// </summary>
    private static List<ChatLine> PlayerLines(ChatLog log)
    {
        List<ChatLine> lines = new();
        for (int i = 0; i < log.Count; i++)
        {
            if (!log[i].IsParty)
            {
                lines.Add(log[i]);
            }
        }

        return lines;
    }

    /// <summary>
    /// The texts of the party's lines of <paramref name="log"/>, oldest first.
    /// </summary>
    private static List<string> PartyLines(ChatLog log)
    {
        List<string> lines = new();
        for (int i = 0; i < log.Count; i++)
        {
            if (log[i].IsParty)
            {
                lines.Add(log[i].Text);
            }
        }

        return lines;
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
    /// Starts the in-process client, reports it joined, with its world loaded when <paramref name="ready"/>, and waits
    /// until both sides see both players.
    /// </summary>
    private IEnumerator Join(bool ready)
    {
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        client = InProcessClient.Start(SceneBootTestHelper.ResolveFromMain<NetworkSession>(), gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        if (ready)
        {
            client.SendReady();
        }
        else
        {
            client.SendJoined();
        }

        yield return SceneBootTestHelper.WaitUntil(
            () => hostPlayers.Players.Count == 2 && client.Players.Players.Count == 2 && client.LocalPlayer.Current != null,
            "both sides to see both players");
    }
}
