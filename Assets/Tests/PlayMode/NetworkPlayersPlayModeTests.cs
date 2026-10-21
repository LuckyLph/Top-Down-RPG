using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests for networked players between a host and an in-process client: spawning on readiness,
/// owner-driven movement under host statuses and how copies animate it (a late joiner included), attacks, disconnects
/// and late-arriving dead players.
/// </summary>
public class NetworkPlayersPlayModeTests
{
    private InProcessClient client;

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
    /// The host spawns a client's player only once the client reports ready, and the client simulates it. The client's
    /// walking, a host-applied slow and stun, and its attacks show on the host's copy, which animates the same gait,
    /// playback rate and direction as the owner from the replicated move and facing (the slowed move replicated as the
    /// actual speed over the base speed); a disconnect drops that copy while the host keeps its own player.
    /// </summary>
    [UnityTest]
    public IEnumerator HostedGame_SpawnsAPlayerForAReadyClient_AndReplicatesItsMovementAndAttacks()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.IsInGame, Is.True);

        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        LocalPlayerTracker hostLocal = gameplay.Resolve<LocalPlayerTracker>();
        Assert.That(hostPlayers.Players.Count, Is.EqualTo(1));
        Assert.That(hostLocal.Current.Controller.GetComponent<NetworkObject>().IsOwner, Is.True, "The host's own player should be spawned and owned by the host.");

        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");

        for (int i = 0; i < 10; i++)
        {
            yield return null;
        }

        Assert.That(client.Players.Players, Is.Empty, "Nothing is sent to a client before it reports ready.");
        Assert.That(hostPlayers.Players.Count, Is.EqualTo(1), "The host waits for the client to be ready before spawning its player.");

        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(
            () => hostPlayers.Players.Count == 2 && client.Players.Players.Count == 2 && client.LocalPlayer.Current != null,
            "both sides to see both players");

        PlayerController clientOwned = client.LocalPlayer.Current.Controller;
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId);
        Assert.That(hostCopy, Is.Not.Null, "The host should hold a copy of the client's player owned by that client.");
        Assert.That(hostCopy.SimulatesMovement, Is.False);
        Assert.That(clientOwned.SimulatesMovement, Is.True);
        Assert.That(InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId), Is.Not.Null, "The client should see the host's player.");
        client.DisableColliders();

        float startX = hostCopy.transform.position.x;
        client.Input.PointAt((Vector2)clientOwned.transform.position + Vector2.right * 50f);
        client.Input.MovePressedThisFrame = true;
        yield return null;
        client.Input.MovePressedThisFrame = false;
        yield return SceneBootTestHelper.WaitUntil(() => hostCopy.transform.position.x > startX + 0.5f, "the client's movement to reach the host");
        Assert.That(hostCopy.FacingDirection.x, Is.GreaterThan(0.9f));
        Assert.That(hostCopy.CurrentMove.x, Is.GreaterThan(0.9f), "The host's copy should show the client walking.");
        CharacterAnimator ownerAnimation = clientOwned.GetComponentInChildren<CharacterVisuals>().Animator;
        CharacterAnimator copyAnimation = hostCopy.GetComponentInChildren<CharacterVisuals>().Animator;
        Assert.That(ownerAnimation.Gait, Is.EqualTo(CharacterGait.Run));
        Assert.That(copyAnimation.Gait, Is.EqualTo(CharacterGait.Run), "The host's copy runs as the owner does.");
        Assert.That(copyAnimation.Direction, Is.EqualTo(CharacterDirections.East));
        Assert.That(copyAnimation.PlaybackRate, Is.EqualTo(ownerAnimation.PlaybackRate).Within(0.02f));

        float fullSpeed = clientOwned.ControlSettings.MoveSpeed;
        StatusEffectService hostStatuses = gameplay.Resolve<StatusEffectService>();
        DamageReceiver hostCopyReceiver = hostCopy.GetComponent<DamageReceiver>();
        Assert.That(hostStatuses.Apply(hostCopyReceiver, SceneBootTestHelper.FindStatus("Chill")), Is.EqualTo(StatusApplyOutcome.Landed));
        PlayerMotor2D ownerMotor = clientOwned.GetComponent<PlayerMotor2D>();
        yield return SceneBootTestHelper.WaitUntil(() => ownerMotor.SpeedScale < 1f, "the owner to apply the host's slow to its own movement");
        yield return new WaitForSeconds(0.3f);
        float slowedStartX = hostCopy.transform.position.x;
        yield return new WaitForSeconds(1f);
        float slowedSpeed = hostCopy.transform.position.x - slowedStartX;
        Assert.That(slowedSpeed, Is.GreaterThan(0.2f * fullSpeed), "A slowed client still walks.");
        Assert.That(slowedSpeed, Is.LessThan(0.8f * fullSpeed), "The host's copy moves at the slowed speed.");
        Assert.That(hostCopy.CurrentMove.magnitude, Is.EqualTo(ownerMotor.SpeedScale).Within(0.02f), "The move replicates the slowed speed over the base speed.");
        Assert.That(copyAnimation.Gait, Is.EqualTo(ownerAnimation.Gait));
        Assert.That(copyAnimation.PlaybackRate, Is.EqualTo(ownerAnimation.PlaybackRate).Within(0.02f), "A slowed copy plays the gait as slowly as its owner.");
        Assert.That(copyAnimation.PlaybackRate, Is.LessThan(fullSpeed / ownerAnimation.Clip.NaturalSpeed - 0.1f));
        Assert.That(copyAnimation.Direction, Is.EqualTo(ownerAnimation.Direction));

        Assert.That(hostStatuses.Apply(hostCopyReceiver, SceneBootTestHelper.FindStatus("Stun")), Is.EqualTo(StatusApplyOutcome.Landed));
        yield return SceneBootTestHelper.WaitUntil(() => clientOwned.CurrentOrder == PlayerOrderKind.Idle, "the stun to clear the owner's orders");
        yield return new WaitForSeconds(0.4f);
        float stunnedX = hostCopy.transform.position.x;
        yield return new WaitForSeconds(0.5f);
        Assert.That(Mathf.Abs(hostCopy.transform.position.x - stunnedX), Is.LessThan(0.05f), "A stunned client's player stands still on the host too.");
        Assert.That(copyAnimation.Gait, Is.EqualTo(CharacterGait.Idle), "A stunned copy stands idle.");
        client.Input.StopPressedThisFrame = true;
        yield return null;
        client.Input.StopPressedThisFrame = false;

        clientOwned.GetComponent<PlayerWeaponController>().TryAttack(Vector2.right);
        yield return SceneBootTestHelper.WaitUntil(() => HasSlashOwnedBy(hostCopy.transform), "the client's attack to play on the host");

        client.Manager.Shutdown();
        yield return SceneBootTestHelper.WaitUntil(() => hostPlayers.Players.Count == 1, "the host to drop the disconnected client's player");
        Assert.That(hostLocal.Current, Is.Not.Null);
    }

    /// <summary>
    /// A player that dies while hidden from a client arrives there dead, with its colliders and controller off, and
    /// both come back on when the host respawns it.
    /// </summary>
    [UnityTest]
    public IEnumerator APlayerThatIsDeadWhenItReachesAClient_ArrivesDead_AndComesBackOnRespawn()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        PlayerController hostPlayer = gameplay.Resolve<LocalPlayerTracker>().Current.Controller;
        NetworkObject hostPlayerObject = hostPlayer.GetComponent<NetworkObject>();

        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(
            () => hostPlayers.Players.Count == 2 && client.Players.Players.Count == 2,
            "both sides to see both players");

        ulong clientId = client.Manager.LocalClientId;
        hostPlayerObject.NetworkHide(clientId);
        yield return SceneBootTestHelper.WaitUntil(
            () => InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId) == null,
            "the client to drop its copy of the host's player");

        gameplay.Resolve<DamageService>().ApplyDamage(hostPlayer.GetComponent<DamageReceiver>(), hostPlayer.GetComponent<Health>().MaxHealth);
        Assert.That(hostPlayer.GetComponent<Health>().IsDead, Is.True);

        hostPlayerObject.NetworkShow(clientId);
        yield return SceneBootTestHelper.WaitUntil(
            () => InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId) != null,
            "the client to receive the dead host player");

        PlayerController copy = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId);
        Assert.That(copy.GetComponent<Health>().IsDead, Is.True);
        Assert.That(AnyColliderEnabled(copy), Is.False, "A player that arrives dead must not block the living like a standing body.");
        Assert.That(copy.enabled, Is.False, "A player that arrives dead must have its gameplay behaviours off, as if this machine saw it die.");

        yield return SceneBootTestHelper.WaitUntil(() => !copy.GetComponent<Health>().IsDead, "the host to respawn its player on the client");
        Assert.That(AnyColliderEnabled(copy), Is.True, "The respawned player must collide again.");
        Assert.That(copy.enabled, Is.True);
    }

    /// <summary>
    /// A client joining while the host's player runs north gets a copy of it that runs at once, in the host's gait,
    /// direction and playback rate, from the replicated move and facing; when the host stops, the copy stands idle
    /// facing the host's way.
    /// </summary>
    [UnityTest]
    public IEnumerator AClientJoiningWhileTheHostRuns_SeesTheHostsPlayerRun_AndStand()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }

        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        PlayerController hostPlayer = gameplay.Resolve<LocalPlayerTracker>().Current.Controller;
        ScriptedCommands hostCommands = new();
        hostPlayer.SetCommandSource(hostCommands);
        hostCommands.Click((Vector2)hostPlayer.transform.position + Vector2.up * 50f);
        yield return new WaitForSeconds(0.2f);
        CharacterAnimator hostAnimation = hostPlayer.GetComponentInChildren<CharacterVisuals>().Animator;
        Assert.That(hostAnimation.Gait, Is.EqualTo(CharacterGait.Run));

        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(
            () => hostPlayers.Players.Count == 2 && InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId) != null,
            "the client to get a copy of the host's player");
        PlayerController copy = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId);
        yield return null;
        yield return null;

        CharacterAnimator copyAnimation = copy.GetComponentInChildren<CharacterVisuals>().Animator;
        Assert.That(hostPlayer.CurrentOrder, Is.EqualTo(PlayerOrderKind.Move), "The host should still be running when the client joins.");
        Assert.That(copyAnimation.Gait, Is.EqualTo(CharacterGait.Run), "A joining client sees a running player run.");
        Assert.That(copyAnimation.Direction, Is.EqualTo(hostAnimation.Direction));
        Assert.That(copyAnimation.PlaybackRate, Is.EqualTo(hostAnimation.PlaybackRate).Within(0.02f));

        hostCommands.Stop();
        yield return SceneBootTestHelper.WaitUntil(() => copyAnimation.Gait == CharacterGait.Idle, "the client's copy to stand when the host stops");
        Assert.That(hostAnimation.Gait, Is.EqualTo(CharacterGait.Idle));
        Assert.That(copyAnimation.Direction, Is.EqualTo(hostAnimation.Direction), "The copy stands facing the host's way.");
    }

    /// <summary>
    /// Whether any <see cref="Collider2D"/> on the unit's own object is enabled.
    /// </summary>
    private static bool AnyColliderEnabled(Component unit)
    {
        foreach (Collider2D unitCollider in unit.GetComponents<Collider2D>())
        {
            if (unitCollider.enabled)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a sword slash rooted on <paramref name="owner"/> exists in the scene.
    /// </summary>
    private static bool HasSlashOwnedBy(Transform owner)
    {
        foreach (SwordSlashAttack slash in Object.FindObjectsByType<SwordSlashAttack>())
        {
            if (slash.OwnerRoot == owner)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <see cref="IPlayerCommandSource"/> that the test feeds one move click or stop at a time.
    /// </summary>
    private sealed class ScriptedCommands : IPlayerCommandSource
    {
        private PlayerCommand next;

        /// <summary>
        /// Queues a single move click on the ground at <paramref name="point"/> for the next read.
        /// </summary>
        public void Click(Vector2 point)
        {
            next = new PlayerCommand(point, default, movePressed: true, moveHeld: false, stopPressed: false);
        }

        /// <summary>
        /// Queues a single stop for the next read.
        /// </summary>
        public void Stop()
        {
            next = new PlayerCommand(Vector2.zero, default, movePressed: false, moveHeld: false, stopPressed: true);
        }

        /// <summary>
        /// Returns the queued command and clears it, so each is read once; an empty command otherwise.
        /// </summary>
        public PlayerCommand ReadCommand()
        {
            PlayerCommand command = next;
            next = default;
            return command;
        }
    }
}
