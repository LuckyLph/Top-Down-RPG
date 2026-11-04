using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// Explicit benchmarks of what one machine shows of another's actions under the delayed connection
/// (<see cref="NetworkDelay"/>, Docs/KnownGaps.md: Phase 4), between a host and an in-process client at each of its
/// settings: how long a copy's windup waits for the owner's contact and how far a copy's cast releases from the host
/// resolving it, how often a client's own attack holds its contact frame and how well its own view would predict the
/// host's hit, and when a remote arrow lands against when its hit reaches that machine. Each logs its numbers and
/// asserts nothing.
/// </summary>
public class NetworkTimingPlayModeTests
{
    private const int BoltSlot = 0;
    private const int WhirlSlot = PlayerAbilities.ClassSlotCount;
    private const int LongbowIndex = 2;
    private const int AttacksPerSetting = 30;
    private const int CastsPerSetting = 14;
    private const int ShotsPerSetting = 20;

    private static readonly NetworkDelay.Conditions[] Settings =
    {
        NetworkDelay.Conditions.None,
        NetworkDelay.Conditions.Fast,
        NetworkDelay.Conditions.Typical,
        NetworkDelay.Conditions.Slow,
    };

    private InProcessClient client;
    private NetworkDelay delay;
    private IObjectResolver gameplay;
    private PlayerRegistry hostPlayers;
    private MobController hostMob;
    private MobController clientMob;

    /// <summary>
    /// Disposes the delayed connection and the in-process client.
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
    /// The client's sword attacks and casts (Whirl and Bolt in turn) as the host's copy of its player shows them: for
    /// each attack, how long past its windup the copy held the last windup frame before the contact message arrived
    /// (negative: the contact cut the windup short), and for each cast, when the copy would release on its own clock
    /// (its start plus the cast time) and when it does release, less the host resolving the cast on the completion
    /// message.
    /// </summary>
    [UnityTest, Explicit, Category("NetworkBench"), Timeout(900000)]
    public IEnumerator Benchmark_ACopysWindupHoldAndCastRelease()
    {
        yield return HostAndJoin(false);
        PlayerController owner = client.LocalPlayer.Current.Controller;
        PlayerController copy = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId);
        PlayerWeaponController ownerWeapon = owner.GetComponent<PlayerWeaponController>();
        PlayerAbilities ownerAbilities = owner.GetComponent<PlayerAbilities>();
        PlayerAbilities copyAbilities = copy.GetComponent<PlayerAbilities>();
        CharacterAnimator copyAnimation = copy.GetComponentInChildren<CharacterVisuals>().Animator;
        float resolvedAt = -1f;
        gameplay.Resolve<AbilityService>().CastResolved += (caster, _) =>
        {
            if (caster == copyAbilities && resolvedAt < 0f)
            {
                resolvedAt = Time.time;
            }
        };

        foreach (NetworkDelay.Conditions conditions in Settings)
        {
            delay.Set(conditions);
            yield return new WaitForSecondsRealtime(0.6f);
            List<float> holds = new();
            float windup = ownerWeapon.CurrentWeapon.Timeline.Windup;
            for (int i = 0; i < AttacksPerSetting; i++)
            {
                yield return SceneBootTestHelper.WaitUntil(() => ownerWeapon.TryAttack(Vector2.right), "the owner's next attack");
                float startedAt = -1f;
                float contactAt = -1f;
                for (float until = Time.time + 3f; Time.time < until && contactAt < 0f;)
                {
                    yield return null;
                    bool attacking = copyAnimation.IsActing && copyAnimation.ActionKind == CharacterActionKind.Attack;
                    if (startedAt < 0f && attacking)
                    {
                        startedAt = Time.time;
                    }

                    if (startedAt >= 0f && attacking && copyAnimation.ActionPhase == AttackPhase.Contact)
                    {
                        contactAt = Time.time;
                    }
                }

                if (startedAt >= 0f && contactAt >= 0f)
                {
                    holds.Add((contactAt - startedAt - windup) * 1000f);
                }

                yield return SceneBootTestHelper.WaitUntil(() => !copyAnimation.IsActing, "the copy's attack to end");
            }

            Debug.Log($"[NetTiming] {conditions}: a copy's held windup past its own (ms), {holds.Count} attacks: {Describe(holds)}; held over 50 ms {Share(holds, value => value > 50f)}, over 100 ms {Share(holds, value => value > 100f)}, over 200 ms {Share(holds, value => value > 200f)}");

            List<float> drifts = new();
            List<float> releases = new();
            for (int i = 0; i < CastsPerSetting; i++)
            {
                int slot = i % 2 == 0 ? WhirlSlot : BoltSlot;
                float castTime = ownerAbilities.GetAbility(slot).CastTime;
                yield return SceneBootTestHelper.WaitUntil(() => ownerAbilities.CanCast(slot, out _), "the next cast to be ready");
                resolvedAt = -1f;
                float startedAt = -1f;
                float releasedAt = -1f;
                client.Input.PointAt((Vector2)owner.transform.position + Vector2.right * 3f);
                client.Input.AbilityPressedThisFrame = slot;
                yield return null;
                client.Input.AbilityPressedThisFrame = -1;
                for (float until = Time.time + 3f; Time.time < until && (resolvedAt < 0f || releasedAt < 0f);)
                {
                    yield return null;
                    bool casting = copyAnimation.IsActing && copyAnimation.ActionKind == CharacterActionKind.Cast;
                    if (startedAt < 0f && casting)
                    {
                        startedAt = Time.time;
                    }

                    if (releasedAt < 0f && casting && copyAnimation.ActionPhase == AttackPhase.Contact)
                    {
                        releasedAt = Time.time;
                    }
                }

                if (resolvedAt >= 0f && startedAt >= 0f)
                {
                    drifts.Add((startedAt + castTime - resolvedAt) * 1000f);
                }

                if (resolvedAt >= 0f && releasedAt >= 0f)
                {
                    releases.Add((releasedAt - resolvedAt) * 1000f);
                }

                yield return SceneBootTestHelper.WaitUntil(() => !copyAnimation.IsActing, "the copy's cast to end");
            }

            Debug.Log($"[NetTiming] {conditions}: a copy's own clock (its start plus the cast time) less the host's resolve (ms), {drifts.Count} casts: {Describe(drifts)}; before the completion arrived by over 30 ms {Share(drifts, value => value < -30f)}, over 60 ms {Share(drifts, value => value < -60f)}; after it by over 30 ms {Share(drifts, value => value > 30f)}; the copy's release less the host's resolve (ms): {Describe(releases)}");
        }
    }

    /// <summary>
    /// The client's sword attacks on the host's mob as the client shows them: against the mob standing still in reach,
    /// how often the client's own player held its contact frame for the hit and how long after its contact the hit
    /// reached it; against the mob sliding across the sword's reach at a Weasel's speed, how often what the client
    /// shows at its contact (its copy of the mob in the hit area) agreed with the host's hit.
    /// </summary>
    [UnityTest, Explicit, Category("NetworkBench"), Timeout(900000)]
    public IEnumerator Benchmark_AClientsContactHoldAndItsPrediction()
    {
        yield return HostAndJoin(true);
        PlayerController owner = client.LocalPlayer.Current.Controller;
        PlayerWeaponController ownerWeapon = owner.GetComponent<PlayerWeaponController>();
        HitFeedback ownerFeedback = owner.GetComponent<HitFeedback>();
        Health mobHealth = hostMob.GetComponent<Health>();
        Health copyHealth = clientMob.GetComponent<Health>();
        Vector2 ownerAt = owner.transform.position;
        Vector2 mobHome = ownerAt + new Vector2(0.8f, 0f);
        int hostHits = 0;
        float hitShownAt = -1f;
        mobHealth.Damaged += _ => hostHits++;
        client.Resolve<CombatEvents>().DamageApplied += report =>
        {
            if (report.Target == copyHealth)
            {
                hitShownAt = Time.time;
            }
        };
        float contactAt = -1f;
        bool predicted = false;
        System.Action<int, Vector2, Vector2, UnitTarget> onContact = (_, direction, position, _) =>
        {
            contactAt = Time.time;
            predicted = InHitArea(ownerWeapon.CurrentWeapon, position, direction, clientMob);
        };
        ownerWeapon.AttackContacted += onContact;
        try
        {
            foreach (NetworkDelay.Conditions conditions in Settings)
            {
                delay.Set(conditions);
                for (int moving = 0; moving < 2; moving++)
                {
                    PlaceHostMob(mobHome);
                    yield return new WaitForSecondsRealtime(0.8f);
                    int held = 0;
                    int landed = 0;
                    int agreed = 0;
                    int predictedOnly = 0;
                    int landedOnly = 0;
                    List<float> lags = new();
                    float sweepStart = Time.time;
                    for (int i = 0; i < AttacksPerSetting; i++)
                    {
                        if (mobHealth.CurrentHealth < mobHealth.MaxHealth / 2)
                        {
                            mobHealth.Restore();
                        }

                        int holdsBefore = ownerFeedback.HoldCount;
                        int hitsBefore = hostHits;
                        contactAt = -1f;
                        hitShownAt = -1f;
                        UnitTarget target = new(copyHealth, clientMob.GetComponent<Collider2D>(), UnitTeam.Enemy);
                        bool started = false;
                        while (!started)
                        {
                            started = ownerWeapon.TryAttack(Vector2.right, target);
                            if (moving == 1)
                            {
                                Slide(mobHome, sweepStart);
                            }

                            yield return null;
                        }

                        float settle = conditions.RoundTripMs / 1000f + 0.3f;
                        for (float until = Time.time + 2f; Time.time < until && (contactAt < 0f || Time.time < contactAt + settle);)
                        {
                            if (moving == 1)
                            {
                                Slide(mobHome, sweepStart);
                            }

                            yield return null;
                        }

                        bool hit = hostHits > hitsBefore;
                        landed += hit ? 1 : 0;
                        held += ownerFeedback.HoldCount > holdsBefore ? 1 : 0;
                        agreed += hit == predicted ? 1 : 0;
                        predictedOnly += predicted && !hit ? 1 : 0;
                        landedOnly += hit && !predicted ? 1 : 0;
                        if (hit && contactAt >= 0f && hitShownAt >= 0f)
                        {
                            lags.Add((hitShownAt - contactAt) * 1000f);
                        }
                    }

                    string label = moving == 1 ? "the mob sliding across the reach" : "the mob standing in reach";
                    Debug.Log($"[NetTiming] {conditions}, {label}: {AttacksPerSetting} attacks, {landed} hits on the host; the client held its contact frame {held} times; its hit showed after its contact (ms): {Describe(lags)}; its own view at contact agreed with the host {agreed} times (it saw a hit the host did not {predictedOnly}, the host hit what it did not see {landedOnly})");
                }
            }
        }
        finally
        {
            ownerWeapon.AttackContacted -= onContact;
        }
    }

    /// <summary>
    /// The host's longbow shots at its mob as the client shows them, the client's projectiles flown each frame: how long
    /// after the host's shot the client's arrow left, whether it struck its copy of the mob, and when the hit reached
    /// the client against when its arrow landed; with the mob standing and sliding across the shot.
    /// </summary>
    [UnityTest, Explicit, Category("NetworkBench"), Timeout(900000)]
    public IEnumerator Benchmark_RemoteArrows()
    {
        yield return HostAndJoin(true);
        PlayerController shooter = gameplay.Resolve<LocalPlayerTracker>().Current.Controller;
        PlayerWeaponController shooterWeapon = shooter.GetComponent<PlayerWeaponController>();
        Assert.That(shooterWeapon.EquipFromCatalog(LongbowIndex), Is.True);
        ProjectileService clientProjectiles = client.Resolve<ProjectileService>();
        Health mobHealth = hostMob.GetComponent<Health>();
        Health copyHealth = clientMob.GetComponent<Health>();
        foreach (Collider2D body in clientMob.GetComponentsInChildren<Collider2D>(true))
        {
            body.enabled = !body.isTrigger;
        }

        float shotAt = -1f;
        float clientLaunchedAt = -1f;
        float clientLandedAt = -1f;
        bool clientStruckCopy = false;
        float hitShownAt = -1f;
        shooterWeapon.AttackContacted += (_, _, _, _) => shotAt = Time.time;
        clientProjectiles.UnitStruck += (unit, _, _) =>
        {
            clientLandedAt = Time.time;
            clientStruckCopy = unit != null && unit.Health == copyHealth;
        };
        client.Resolve<CombatEvents>().DamageApplied += report =>
        {
            if (report.Target == copyHealth)
            {
                hitShownAt = Time.time;
            }
        };

        Vector2 mobHome = (Vector2)shooter.transform.position + new Vector2(4f, 0f);
        foreach (NetworkDelay.Conditions conditions in Settings)
        {
            delay.Set(conditions);
            for (int moving = 0; moving < 2; moving++)
            {
                PlaceHostMob(mobHome);
                yield return new WaitForSecondsRealtime(0.8f);
                List<float> starts = new();
                List<float> gaps = new();
                int struck = 0;
                int hits = 0;
                float sweepStart = Time.time;
                for (int i = 0; i < ShotsPerSetting; i++)
                {
                    if (mobHealth.CurrentHealth < mobHealth.MaxHealth / 2)
                    {
                        mobHealth.Restore();
                    }

                    shotAt = clientLaunchedAt = clientLandedAt = hitShownAt = -1f;
                    clientStruckCopy = false;
                    int flyingBefore = clientProjectiles.FlyingCount;
                    UnitTarget target = new(mobHealth, hostMob.GetComponent<Collider2D>(), UnitTeam.Enemy);
                    bool started = false;
                    while (!started)
                    {
                        started = shooterWeapon.TryAttack(Vector2.right, target);
                        Step(moving == 1, mobHome, sweepStart, clientProjectiles);
                        yield return null;
                    }

                    float settle = conditions.RoundTripMs / 1000f + 0.8f;
                    for (float until = Time.time + 2.5f; Time.time < until && (clientLandedAt < 0f || hitShownAt < 0f) && (shotAt < 0f || Time.time < shotAt + settle);)
                    {
                        Step(moving == 1, mobHome, sweepStart, clientProjectiles);
                        if (clientLaunchedAt < 0f && (clientProjectiles.FlyingCount > flyingBefore || clientLandedAt >= 0f))
                        {
                            clientLaunchedAt = Time.time;
                        }

                        yield return null;
                    }

                    struck += clientStruckCopy ? 1 : 0;
                    hits += hitShownAt >= 0f ? 1 : 0;
                    if (shotAt >= 0f && clientLaunchedAt >= 0f)
                    {
                        starts.Add((clientLaunchedAt - shotAt) * 1000f);
                    }

                    if (clientStruckCopy && hitShownAt >= 0f)
                    {
                        gaps.Add((hitShownAt - clientLandedAt) * 1000f);
                    }
                }

                string label = moving == 1 ? "the mob sliding across the shot" : "the mob standing";
                Debug.Log($"[NetTiming] {conditions}, {label}: {ShotsPerSetting} host shots; the client's arrow left after the host's shot (ms): {Describe(starts)}; it struck the copy {struck} times, the hit reached the client {hits} times; the hit showed after the client's arrow landed (ms): {Describe(gaps)}");
            }
        }
    }

    /// <summary>
    /// Boots to the menu, installs the delayed connection, hosts with one mob kept (its AI, motor and body still, the
    /// others deactivated) and joins an in-process client, waiting until both machines see both players and the client
    /// its copy of the mob; the colliders of the client's player and mob copies are off. With
    /// <paramref name="mobInReach"/> the mob goes beside the client's player.
    /// </summary>
    private IEnumerator HostAndJoin(bool mobInReach)
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        delay = NetworkDelay.Install();
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
                mob.GetComponent<MobMotor2D>().enabled = false;
                mob.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
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
        foreach (Collider2D body in clientMob.GetComponentsInChildren<Collider2D>())
        {
            body.enabled = false;
        }

        if (mobInReach)
        {
            PlaceHostMob((Vector2)client.LocalPlayer.Current.Transform.position + new Vector2(0.8f, 0f));
        }
    }

    /// <summary>
    /// Puts the host's mob at <paramref name="position"/>.
    /// </summary>
    private void PlaceHostMob(Vector2 position)
    {
        hostMob.GetComponent<Rigidbody2D>().position = position;
        hostMob.transform.position = position;
        Physics2D.SyncTransforms();
    }

    /// <summary>
    /// Slides the host's mob up and down across <paramref name="home"/>, 1.6 units either way at a Weasel's 2 units per
    /// second at its fastest, timed from <paramref name="since"/>.
    /// </summary>
    private void Slide(Vector2 home, float since)
    {
        const float Reach = 1.6f;
        const float Speed = 2f;
        PlaceHostMob(home + Vector2.up * (Reach * Mathf.Sin((Time.time - since) * Speed / Reach)));
    }

    /// <summary>
    /// One frame of the arrows benchmark: slides the mob when <paramref name="moving"/> and flies the client's arrows.
    /// </summary>
    private void Step(bool moving, Vector2 home, float since, ProjectileService clientProjectiles)
    {
        if (moving)
        {
            Slide(home, since);
        }

        clientProjectiles.Tick();
    }

    /// <summary>
    /// Whether <paramref name="copy"/>'s body lies in <paramref name="weapon"/>'s hit area for an attack from
    /// <paramref name="position"/> toward <paramref name="direction"/>, as this machine shows it: its colliders are
    /// turned on for the query alone, so they meet nothing in the shared scene.
    /// </summary>
    private static bool InHitArea(PlayerWeapon weapon, Vector2 position, Vector2 direction, MobController copy)
    {
        Collider2D[] bodies = copy.GetComponentsInChildren<Collider2D>(true);
        foreach (Collider2D body in bodies)
        {
            body.enabled = !body.isTrigger;
        }

        Physics2D.SyncTransforms();
        Vector2 aim = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;
        bool inside = false;
        foreach (Collider2D found in Physics2D.OverlapBoxAll(weapon.HitCentre(position, aim), weapon.HitSize, Vector2.SignedAngle(Vector2.up, aim)))
        {
            inside |= !found.isTrigger && found.GetComponentInParent<MobController>() == copy;
        }

        foreach (Collider2D body in bodies)
        {
            body.enabled = false;
        }

        return inside;
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

    /// <summary>
    /// The median, 10th and 90th percentiles and range of <paramref name="values"/>.
    /// </summary>
    private static string Describe(List<float> values)
    {
        if (values.Count == 0)
        {
            return "none";
        }

        float[] sorted = values.ToArray();
        System.Array.Sort(sorted);
        return $"median {At(sorted, 0.5f):0}, p10 {At(sorted, 0.1f):0}, p90 {At(sorted, 0.9f):0}, min {sorted[0]:0}, max {sorted[sorted.Length - 1]:0}";
    }

    /// <summary>
    /// The value at <paramref name="share"/> of sorted <paramref name="sorted"/>.
    /// </summary>
    private static float At(float[] sorted, float share)
    {
        return sorted[Mathf.Clamp(Mathf.RoundToInt(share * (sorted.Length - 1)), 0, sorted.Length - 1)];
    }

    /// <summary>
    /// How many of <paramref name="values"/> match, as "n of count".
    /// </summary>
    private static string Share(List<float> values, System.Func<float, bool> match)
    {
        int count = 0;
        foreach (float value in values)
        {
            count += match(value) ? 1 : 0;
        }

        return $"{count} of {values.Count}";
    }
}
