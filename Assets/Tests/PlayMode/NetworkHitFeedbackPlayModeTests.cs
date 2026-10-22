using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests for hit feedback online (Docs/Animation.md, Phase 6) between a host and an in-process client, whose
/// own container runs its own <see cref="HitFeedbackPresenter"/> with a recording camera: a client's sword hit on a
/// Weasel flashing and nudging it on both machines, away from the attacker on the client too (the hit names its source
/// over the wire), and kicking the client's camera; the host's hit on the client's player kicking the client's camera
/// only.
/// </summary>
public class NetworkHitFeedbackPlayModeTests
{
    private InProcessClient client;
    private IObjectResolver gameplay;
    private PlayerRegistry hostPlayers;
    private MobController hostMob;
    private MobController clientMob;

    /// <summary>
    /// Disposes the in-process client when the test started one.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        client?.Dispose();
        client = null;
    }

    /// <summary>
    /// The client's player swings its sword at a Weasel beside it: the host resolves the hit, and both the host's Weasel
    /// and the client's copy flash and are nudged away from the attacker; the client, whose hit it was, kicks its camera
    /// toward the Weasel.
    /// </summary>
    [UnityTest]
    public IEnumerator AClientsHitOnAMob_FlashesAndNudgesItOnBothMachines()
    {
        yield return HostAndJoinWithOneMob();
        PlayerController clientOwned = client.LocalPlayer.Current.Controller;
        PlayerWeaponController ownerWeapon = clientOwned.GetComponent<PlayerWeaponController>();
        Vector2 owner = clientOwned.transform.position;
        yield return PlaceHostMob(owner + new Vector2(0.8f, 0f));
        HitFeedback hostStruck = hostMob.GetComponent<HitFeedback>();
        HitFeedback clientStruck = clientMob.GetComponent<HitFeedback>();
        int kicks = client.CameraKick.Count;

        Assert.That(ownerWeapon.TryAttack(Vector2.right), Is.True);
        yield return SceneBootTestHelper.WaitUntil(() => hostStruck.NudgeCount > 0 && clientStruck.NudgeCount > 0, "the hit to show on both machines");

        Assert.That(hostStruck.FlashCount, Is.EqualTo(1), "The host's Weasel flashes.");
        Assert.That(hostStruck.LastNudgeDirection.x, Is.GreaterThan(0.9f), "The host nudges it away from the attacker.");
        Assert.That(clientStruck.FlashCount, Is.EqualTo(1), "The client's copy flashes.");
        Assert.That(clientStruck.LastNudgeDirection.x, Is.GreaterThan(0.9f), "The client nudges it away from the attacker too.");
        Assert.That(client.CameraKick.Count, Is.EqualTo(kicks + 1), "The client's own hit kicks its camera.");
        Assert.That(client.CameraKick.LastOffset.x, Is.GreaterThan(0f), "Toward the Weasel.");
    }

    /// <summary>
    /// The host resolving the Weasel's hit on the client's player kicks the client's camera, away from the Weasel, and
    /// not the host's, whose player took and landed nothing.
    /// </summary>
    [UnityTest]
    public IEnumerator TheHostsHitOnTheClientsPlayer_KicksTheClientsCameraOnly()
    {
        yield return HostAndJoinWithOneMob();
        PlayerController clientOwned = client.LocalPlayer.Current.Controller;
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId);
        yield return PlaceHostMob((Vector2)clientOwned.transform.position + new Vector2(0.8f, 0f));
        CameraFollow2D hostCamera = SceneBootTestHelper.ResolveFromMain<CameraFollow2D>();
        DamageService hostDamage = gameplay.Resolve<DamageService>();
        int kicks = client.CameraKick.Count;

        hostDamage.ApplyDamage(hostCopy.GetComponent<DamageReceiver>(), 1, DamageType.Physical, hostMob.gameObject);
        Assert.That(hostCamera.KickOffset, Is.EqualTo(Vector2.zero), "The host's camera does not kick for another player's hit.");
        yield return SceneBootTestHelper.WaitUntil(() => client.CameraKick.Count > kicks, "the hit to kick the client's camera");

        Assert.That(client.CameraKick.Count, Is.EqualTo(kicks + 1));
        Assert.That(client.CameraKick.LastOffset.x, Is.LessThan(0f), "Away from the Weasel.");
        Assert.That(Mathf.Abs(client.CameraKick.LastOffset.y), Is.LessThan(Mathf.Abs(client.CameraKick.LastOffset.x)));
        Assert.That(hostCamera.KickOffset, Is.EqualTo(Vector2.zero));
    }

    /// <summary>
    /// Hosts from the menu keeping one mob with its AI off (the others deactivated), then starts an in-process client and
    /// waits until both machines see both players and the client has its copy of the mob, with the colliders of the
    /// client's player and mob copies off so they meet nothing in the shared scene.
    /// </summary>
    private IEnumerator HostAndJoinWithOneMob()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        hostPlayers = gameplay.Resolve<PlayerRegistry>();
        hostMob = null;
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            if (hostMob == null)
            {
                hostMob = mob;
                mob.enabled = false;
                continue;
            }

            mob.gameObject.SetActive(false);
        }

        Assert.That(hostMob, Is.Not.Null, "A mob should be loaded around the start.");
        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(
            () => hostPlayers.Players.Count == 2 && client.Players.Players.Count == 2 && client.LocalPlayer.Current != null
                  && (clientMob = FindCopyManagedBy(client.Manager, hostMob)) != null,
            "both sides to see both players and the client its copy of the mob");
        client.DisableColliders();
        foreach (MobController copy in Object.FindObjectsByType<MobController>())
        {
            NetworkObject networkObject = copy.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.NetworkManager == client.Manager)
            {
                foreach (Collider2D body in copy.GetComponentsInChildren<Collider2D>())
                {
                    body.enabled = false;
                }
            }
        }
    }

    /// <summary>
    /// Puts the host's mob at <paramref name="position"/>, at rest, and waits for the client's copy to follow.
    /// </summary>
    private IEnumerator PlaceHostMob(Vector2 position)
    {
        Rigidbody2D body = hostMob.GetComponent<Rigidbody2D>();
        body.position = position;
        body.linearVelocity = Vector2.zero;
        hostMob.transform.position = position;
        Physics2D.SyncTransforms();
        yield return SceneBootTestHelper.WaitUntil(
            () => Vector2.Distance(clientMob.transform.position, position) < 0.02f,
            "the client's copy of the mob to follow it");
        yield return new WaitForSeconds(0.2f);
    }

    /// <summary>
    /// The spawned copy of <paramref name="original"/> (the same network object) that belongs to
    /// <paramref name="manager"/>, or null.
    /// </summary>
    private static MobController FindCopyManagedBy(NetworkManager manager, MobController original)
    {
        ulong id = original.GetComponent<NetworkObject>().NetworkObjectId;
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            NetworkObject networkObject = mob.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned && networkObject.NetworkManager == manager && networkObject.NetworkObjectId == id)
            {
                return mob;
            }
        }

        return null;
    }
}
