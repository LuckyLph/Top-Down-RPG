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
/// only. Under a delayed connection (Docs/KnownGaps.md, Phase 4): a client's arrow, its shooter moved off after the
/// shot, nudging the Weasel and kicking the camera along the shot on both machines, the hit carrying where it was shot
/// from; and a client's own sword hit holding its contact frame at its contact, a round trip before the host's hit
/// reaches it.
/// </summary>
public class NetworkHitFeedbackPlayModeTests
{
    private const int LongbowIndex = 2;

    private InProcessClient client;
    private NetworkDelay delay;
    private IObjectResolver gameplay;
    private PlayerRegistry hostPlayers;
    private MobController hostMob;
    private MobController clientMob;

    /// <summary>
    /// Disposes the delayed connection and the in-process client when the test started them.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        delay?.Dispose();
        delay = null;
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
    /// Under a typical delayed connection, the client's player shoots its longbow at the Weasel three units east and is
    /// moved three units south as the arrow leaves: when the host's arrow lands, the host already shows the shooter
    /// there, yet the Weasel is nudged east on both machines and the client's camera kicks east, along the shot, since
    /// the hit carries the point it was shot from rather than each machine's view of the shooter.
    /// </summary>
    [UnityTest]
    public IEnumerator AMovingShootersArrow_TurnsItsHitTheSameWayOnBothMachines()
    {
        yield return HostAndJoinWithOneMob(true);
        PlayerController clientOwned = client.LocalPlayer.Current.Controller;
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId);
        PlayerWeaponController ownerWeapon = clientOwned.GetComponent<PlayerWeaponController>();
        Assert.That(ownerWeapon.EquipFromCatalog(LongbowIndex), Is.True);
        yield return SceneBootTestHelper.WaitUntil(() => hostCopy.GetComponent<PlayerWeaponController>().CurrentWeapon == ownerWeapon.CurrentWeapon, "the host's copy to equip the longbow");
        Vector2 shotFrom = clientOwned.transform.position;
        yield return PlaceHostMob(shotFrom + new Vector2(3f, 0f));
        delay.Set(NetworkDelay.Conditions.Typical);
        yield return new WaitForSecondsRealtime(0.3f);
        HitFeedback hostStruck = hostMob.GetComponent<HitFeedback>();
        HitFeedback clientStruck = clientMob.GetComponent<HitFeedback>();
        int kicks = client.CameraKick.Count;
        Vector2 copyWhenHit = Vector2.zero;
        hostMob.GetComponent<Health>().Damaged += _ => copyWhenHit = hostCopy.transform.position;
        Vector2 movedTo = shotFrom + Vector2.down * 3f;
        System.Action<int, Vector2, Vector2, UnitTarget> moveOff = (_, _, _, _) => clientOwned.Teleport(movedTo);
        ownerWeapon.AttackContacted += moveOff;
        try
        {
            UnitTarget target = new(clientMob.GetComponent<Health>(), null, UnitTeam.Enemy);
            Assert.That(ownerWeapon.TryAttack(Vector2.right, target), Is.True);
            yield return SceneBootTestHelper.WaitUntil(() => hostStruck.NudgeCount > 0 && clientStruck.NudgeCount > 0, "the arrow's hit to show on both machines");
        }
        finally
        {
            ownerWeapon.AttackContacted -= moveOff;
        }

        Assert.That(Vector2.Distance(copyWhenHit, movedTo), Is.LessThan(0.5f), "The host showed the shooter moved off when its arrow landed.");
        Assert.That(hostStruck.LastNudgeDirection.x, Is.GreaterThan(0.99f), $"The host nudges along the shot, not from the moved shooter ({hostStruck.LastNudgeDirection}).");
        Assert.That(clientStruck.LastNudgeDirection.x, Is.GreaterThan(0.99f), $"The client nudges along the shot too ({clientStruck.LastNudgeDirection}).");
        Assert.That(Vector2.Angle(hostStruck.LastNudgeDirection, clientStruck.LastNudgeDirection), Is.LessThan(1f), "Both machines turn the hit alike.");
        Assert.That(client.CameraKick.Count, Is.EqualTo(kicks + 1), "The client's own hit kicks its camera.");
        Assert.That(Vector2.Angle(client.CameraKick.LastOffset, Vector2.right), Is.LessThan(1f), "Along the shot.");
    }

    /// <summary>
    /// Under a typical delayed connection, the client's player swings its sword at the Weasel beside it: its own
    /// prediction holds its contact frame at its contact, before the host's hit has reached it (the client's copy of the
    /// Weasel has not flashed yet); the host's hit then shows on the Weasel a round trip later without holding the
    /// attacker again.
    /// </summary>
    [UnityTest]
    public IEnumerator AClientsSwordHit_HoldsItsContactFrame_AtItsContact()
    {
        yield return HostAndJoinWithOneMob(true);
        PlayerController clientOwned = client.LocalPlayer.Current.Controller;
        PlayerWeaponController ownerWeapon = clientOwned.GetComponent<PlayerWeaponController>();
        HitFeedback ownerFeedback = clientOwned.GetComponent<HitFeedback>();
        yield return PlaceHostMob((Vector2)clientOwned.transform.position + new Vector2(0.8f, 0f));
        delay.Set(NetworkDelay.Conditions.Typical);
        yield return new WaitForSecondsRealtime(0.3f);
        HitFeedback clientStruck = clientMob.GetComponent<HitFeedback>();
        int holdsBefore = ownerFeedback.HoldCount;
        int holdsAtContact = -1;
        int flashesAtContact = -1;
        System.Action<int, Vector2, Vector2, UnitTarget> atContact = (_, _, _, _) =>
        {
            holdsAtContact = ownerFeedback.HoldCount;
            flashesAtContact = clientStruck.FlashCount;
        };
        ownerWeapon.AttackContacted += atContact;
        try
        {
            Assert.That(ownerWeapon.TryAttack(Vector2.right), Is.True);
            yield return SceneBootTestHelper.WaitUntil(() => clientStruck.FlashCount > 0, "the host's hit to reach the client");
        }
        finally
        {
            ownerWeapon.AttackContacted -= atContact;
        }

        Assert.That(holdsAtContact, Is.EqualTo(holdsBefore + 1), "The client holds its contact frame at its contact.");
        Assert.That(flashesAtContact, Is.Zero, "Before the host's hit reached it.");
        Assert.That(ownerFeedback.HoldCount, Is.EqualTo(holdsBefore + 1), "The host's hit, a round trip later, does not hold it again.");
    }

    /// <summary>
    /// Hosts from the menu keeping one mob with its AI off (the others deactivated), then starts an in-process client and
    /// waits until both machines see both players and the client has its copy of the mob, with the colliders of the
    /// client's player and mob copies off so they meet nothing in the shared scene; with <paramref name="delayed"/> the
    /// delayed connection is installed before hosting, with no delay yet.
    /// </summary>
    private IEnumerator HostAndJoinWithOneMob(bool delayed = false)
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        if (delayed)
        {
            delay = NetworkDelay.Install();
        }

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
