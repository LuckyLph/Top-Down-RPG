using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

public class NetworkAbilitiesPlayModeTests
{
    private const int AllySlot = 2;

    private InProcessClient client;

    [TearDown]
    public void TearDown()
    {
        client?.Dispose();
        client = null;
    }

    [UnityTest]
    public IEnumerator AHostCastReachesClientsWithoutBeingAppliedThere_AndAClientsEnemyCastResolvesTheMobOnTheHost()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        PlayerController hostPlayer = gameplay.Resolve<LocalPlayerTracker>().Current.Controller;
        List<(PlayerAbilities Caster, AbilityCast Cast)> applied = new();
        gameplay.Resolve<AbilityService>().CastApplied += (caster, cast) => applied.Add((caster, cast));

        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(
            () => hostPlayers.Players.Count == 2 && client.Players.Players.Count == 2 && client.LocalPlayer.Current != null,
            "both sides to see both players");
        int clientApplied = 0;
        client.Resolve<AbilityService>().CastApplied += (_, _) => clientApplied++;
        PlayerAbilities hostPlayerOnClient = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId).GetComponent<PlayerAbilities>();

        PlayerAbilities hostAbilities = hostPlayer.GetComponent<PlayerAbilities>();
        Assert.That(hostAbilities.TryCast(0, new CastAim(Vector2.zero, Vector2.right, default)), Is.EqualTo(CastOutcome.Started));
        hostAbilities.EndCast(0);
        yield return SceneBootTestHelper.WaitUntil(
            () => hostPlayerOnClient.TryGetRecentCast(0, out AbilityCast remoteCast, out bool remote) && remote && remoteCast.Slot == 0,
            "the host's cast to reach the client's copy of the host's player");
        Assert.That(applied.Count, Is.EqualTo(1));
        Assert.That(clientApplied, Is.Zero, "Only the host applies casts.");

        MobController hostMob = null;
        MobController mobOnClient = null;
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            if (mob.enabled)
            {
                hostMob = mob;
            }
            else
            {
                mobOnClient = mob;
            }
        }

        Assert.That(hostMob, Is.Not.Null);
        Assert.That(mobOnClient, Is.Not.Null, "The client's copy of the mob has its AI off.");
        PlayerController clientOwned = client.LocalPlayer.Current.Controller;
        clientOwned.Teleport((Vector2)mobOnClient.transform.position + Vector2.right * 1.5f);
        yield return null;
        Physics2D.SyncTransforms();

        const int enemySlot = 3;
        Assert.That(clientOwned.GetComponent<PlayerAbilities>().GetAbility(enemySlot).UnitFilter, Is.EqualTo(AbilityUnitFilter.Enemy));
        client.Input.PointAt((Vector2)mobOnClient.transform.position + Vector2.up * 0.15f);
        client.Input.AbilityPressedThisFrame = enemySlot;
        yield return null;
        client.Input.AbilityPressedThisFrame = -1;

        yield return SceneBootTestHelper.WaitUntil(() => applied.Count >= 2, "the client's enemy cast to reach the host");
        Assert.That(applied[1].Cast.Slot, Is.EqualTo(enemySlot));
        Assert.That(applied[1].Cast.Aim.Target.Health, Is.SameAs(hostMob.GetComponent<Health>()), "The mob reference resolves to the host's mob.");
        Assert.That(applied[1].Cast.Aim.Target.Team, Is.EqualTo(UnitTeam.Enemy));
        Assert.That(clientApplied, Is.Zero);
    }

    [UnityTest]
    public IEnumerator Casts_ReachTheHostsAbilityServiceExactlyOnce_IncludingAClientsCastOnTheHostsPlayer()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        PlayerController hostPlayer = gameplay.Resolve<LocalPlayerTracker>().Current.Controller;
        List<(PlayerAbilities Caster, AbilityCast Cast)> applied = new();
        gameplay.Resolve<AbilityService>().CastApplied += (caster, cast) => applied.Add((caster, cast));

        PlayerAbilities hostAbilities = hostPlayer.GetComponent<PlayerAbilities>();
        Assert.That(hostAbilities.TryCast(0, new CastAim(Vector2.zero, Vector2.right, default)), Is.EqualTo(CastOutcome.Started));
        hostAbilities.EndCast(0);
        Assert.That(applied.Count, Is.EqualTo(1), "The host's own cast is applied directly.");
        Assert.That(applied[0].Caster, Is.SameAs(hostAbilities));

        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(
            () => hostPlayers.Players.Count == 2 && client.Players.Players.Count == 2 && client.LocalPlayer.Current != null,
            "both sides to see both players");

        PlayerController clientOwned = client.LocalPlayer.Current.Controller;
        PlayerController hostPlayerOnClient = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId);
        PlayerController clientPlayerOnHost = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId);
        Assert.That(clientOwned.GetComponent<PlayerAbilities>().GetAbility(AllySlot).UnitFilter, Is.EqualTo(AbilityUnitFilter.Ally));
        clientOwned.Teleport((Vector2)hostPlayerOnClient.transform.position + Vector2.right * 2f);
        yield return null;
        Physics2D.SyncTransforms();

        client.Input.PointAt(hostPlayerOnClient.transform.position);
        client.Input.AbilityPressedThisFrame = AllySlot;
        yield return null;
        client.Input.AbilityPressedThisFrame = -1;
        Assert.That(clientOwned.CurrentOrder, Is.EqualTo(PlayerOrderKind.Casting), "The client should cast on the host's player as an ally.");

        yield return SceneBootTestHelper.WaitUntil(() => applied.Count >= 2, "the client's cast to reach the host's AbilityService");
        for (int i = 0; i < 30; i++)
        {
            yield return null;
        }

        Assert.That(applied.Count, Is.EqualTo(2), "Each cast is applied exactly once, on the host.");
        Assert.That(applied[1].Caster, Is.SameAs(clientPlayerOnHost.GetComponent<PlayerAbilities>()));
        Assert.That(applied[1].Cast.Slot, Is.EqualTo(AllySlot));
        Assert.That(applied[1].Cast.Aim.Target.Health, Is.SameAs(hostPlayer.GetComponent<Health>()), "The target arrives as the host's own player.");
        Assert.That(applied[1].Cast.Aim.Target.Team, Is.EqualTo(UnitTeam.Ally));

        Vector2 aim = applied[1].Cast.Aim.Direction;
        Assert.That(aim.x, Is.LessThan(-0.9f), "The client aimed left, at the host's player.");
        Assert.That(Vector2.Dot(clientPlayerOnHost.FacingDirection, aim), Is.GreaterThan(0.9f), "The client's facing toward its aim reaches the host's copy.");
    }
}
