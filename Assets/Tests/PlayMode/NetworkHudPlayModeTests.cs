using System.Collections;
using NUnit.Framework;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode test for the ability bar's answers to a press on a joined client (Docs/UiRevamp.md, Phase 1), with the
/// in-process host and client: the client's own orders and abilities run on the client, so a bar bound to the client's
/// local player frames its running cast (Mend on the host's player), brackets its buffered one, says "Already casting"
/// and "On cooldown", and
/// hatches every slot and says "Stunned" once the host's stun reaches the client's player, while the host's own bar
/// shows none of it.
/// </summary>
public class NetworkHudPlayModeTests
{
    private const string StunPath = "Assets/Data/StatusEffects/Status_Stun.asset";
    private const int MendSlot = 2;
    private const int WhirlSlot = PlayerAbilities.ClassSlotCount;
    private const int LungeSlot = PlayerAbilities.ClassSlotCount + 1;

    private InProcessClient client;
    private AbilityBarPresenter presenter;
    private AbilityBarView clientBar;
    private TMP_Text clientMessage;

    /// <summary>
    /// Disposes the client's bar presenter, destroys its bar and message line, and disposes the in-process client.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        presenter?.Dispose();
        presenter = null;
        if (clientBar != null)
        {
            Object.Destroy(clientBar.gameObject);
        }

        if (clientMessage != null)
        {
            Object.Destroy(clientMessage.gameObject);
        }

        client?.Dispose();
        client = null;
    }

    /// <summary>
    /// The client casts Mend on the host's player: its bar frames Mend. Lunge pressed during it says "Already casting",
    /// and Whirl pressed during it waits with its brackets until Mend ends, then runs, framed, its brackets gone; Mend
    /// pressed again says "On cooldown". The host stuns the client's player: once the stun reaches the client every slot is hatched and a
    /// press says "Stunned", and the hatch goes with the stun. The host's own bar shows none of the client's state.
    /// </summary>
    [UnityTest]
    public IEnumerator AJoinedClientsBar_SaysWhy_AndShowsItsCastingQueuedAndBlockedSlots()
    {
        yield return HostAndJoin();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        AbilityBarView hostBar = Object.FindAnyObjectByType<AbilityBarView>();
        clientBar = Object.Instantiate(hostBar, hostBar.transform.parent);
        clientMessage = Object.Instantiate(hostBar.MessageText, hostBar.transform.parent);
        AbilitySlotView[] slots = clientBar.GetComponentsInChildren<AbilitySlotView>(true);
        clientBar.ConfigureReferences(slots, clientMessage);
        presenter = new AbilityBarPresenter(client.LocalPlayer, clientBar, client.Resolve<IClock>(), gameplay.Resolve<IInputBindingLabels>(), gameplay.Resolve<HudSettings>());
        presenter.Start();
        PlayerController clientOwned = client.LocalPlayer.Current.Controller;
        PlayerAbilities abilities = clientOwned.GetComponent<PlayerAbilities>();
        Assert.That(clientOwned.SimulatesMovement, Is.True, "The client runs its own player's orders.");
        Assert.That(abilities.GetAbility(MendSlot).CastTime, Is.GreaterThanOrEqualTo(0.5f), "Mend casts long enough for three presses.");
        Assert.That(abilities.GetAbility(WhirlSlot).Bufferable, Is.True, "The sword's first ability buffers.");
        Assert.That(abilities.GetAbility(LungeSlot).Bufferable, Is.False);
        PlayerController hostPlayerOnClient = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId);
        clientOwned.Teleport((Vector2)hostPlayerOnClient.transform.position + Vector2.right * 2f);
        yield return null;
        Physics2D.SyncTransforms();
        client.Input.PointAt(hostPlayerOnClient.transform.position);

        yield return Press(MendSlot);
        float mendEnds = Time.time + abilities.GetAbility(MendSlot).CastTime;
        Assert.That(slots[MendSlot].IsCasting, Is.True, "The client's bar frames its running cast, Mend on the host's player.");

        yield return Press(LungeSlot);
        Assert.That(clientBar.IsMessageShown, Is.True);
        Assert.That(clientMessage.text, Is.EqualTo(CastFailureMessages.AlreadyCasting));

        float bufferWindow = clientOwned.ControlSettings.CastBufferWindow;
        yield return SceneBootTestHelper.WaitUntil(() => Time.time >= mendEnds - bufferWindow * 0.5f, "Mend to come within half the buffer window of its end");
        yield return Press(WhirlSlot);
        Assert.That(slots[WhirlSlot].IsQueued, Is.True, "The buffered cast shows its brackets.");

        yield return SceneBootTestHelper.WaitUntil(() => !slots[MendSlot].IsCasting, "Mend to end on the client");
        Assert.That(slots[WhirlSlot].IsQueued, Is.False);
        Assert.That(slots[WhirlSlot].IsCasting, Is.True, "The buffered cast runs when Mend ends.");
        yield return SceneBootTestHelper.WaitUntil(() => !slots[WhirlSlot].IsCasting, "Whirl to end on the client");

        yield return Press(MendSlot);
        Assert.That(clientMessage.text, Is.EqualTo(CastFailureMessages.OnCooldown));

        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(gameplay.Resolve<PlayerRegistry>(), client.Manager.LocalClientId);
        StatusEffectDefinition stun = AssetDatabase.LoadAssetAtPath<StatusEffectDefinition>(StunPath);
        Assert.That(gameplay.Resolve<StatusEffectService>().Apply(hostCopy.GetComponent<DamageReceiver>(), stun), Is.EqualTo(StatusApplyOutcome.Landed));
        yield return SceneBootTestHelper.WaitUntil(() => slots[MendSlot].IsDisabled, "the host's stun to hatch the client's slots");
        for (int slot = 0; slot < slots.Length; slot++)
        {
            Assert.That(slots[slot].IsDisabled, Is.EqualTo(!slots[slot].IsEmpty), $"Slot {slot + 1} is hatched while stunned.");
        }

        yield return Press(WhirlSlot);
        Assert.That(clientMessage.text, Is.EqualTo(CastFailureMessages.Stunned));

        foreach (AbilitySlotView hostSlot in hostBar.GetComponentsInChildren<AbilitySlotView>(true))
        {
            Assert.That(hostSlot.IsDisabled || hostSlot.IsQueued || hostSlot.IsCasting, Is.False, $"The host's {hostSlot.name} shows none of the client's state.");
        }

        Assert.That(hostBar.IsMessageShown, Is.False, "The client's failures stay on the client.");
        yield return SceneBootTestHelper.WaitUntil(() => !slots[MendSlot].IsDisabled, "the stun's end to reach the client");
        foreach (AbilitySlotView slot in slots)
        {
            Assert.That(slot.IsDisabled, Is.False);
        }
    }

    /// <summary>
    /// Presses <paramref name="slot"/> on the client for the next frame, whose update the client's orders read it in.
    /// </summary>
    private IEnumerator Press(int slot)
    {
        client.Input.AbilityPressedThisFrame = slot;
        yield return null;
        client.Input.AbilityPressedThisFrame = -1;
    }

    /// <summary>
    /// Hosts from the menu with every mob deactivated, then starts an in-process client and waits until both machines
    /// see both players.
    /// </summary>
    private IEnumerator HostAndJoin()
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
        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(
            () => hostPlayers.Players.Count == 2 && client.Players.Players.Count == 2 && client.LocalPlayer.Current != null,
            "both sides to see both players");
    }
}
