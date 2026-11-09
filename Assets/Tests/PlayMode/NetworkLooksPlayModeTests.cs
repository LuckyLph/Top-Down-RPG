using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests for players' looks online (Docs/AsepriteCharacters.md, Phase 2) between a host and an in-process
/// client: a client choosing the Bandit in its settings reaches the host's copy of its player, and back to the adventurer;
/// and a client joining after the host chose the Bandit sees the host's player as the Bandit from its spawn, its attacks
/// playing the Bandit's clip. Its colours (Docs/PlayerCharacter.md, Phase 5) go the same way: a client's red tunic
/// reaches the host's copy, drawn in its palette, and its own colours again after a reset; and a client joining after
/// the host picked blonde hair sees it from the spawn.
/// </summary>
public class NetworkLooksPlayModeTests
{
    private const string Bandit = "Bandit";
    private const string Adventurer = "Adventurer";

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
    /// The client's own player draws the Bandit as soon as the client's settings choose it, and the host's copy of it
    /// follows; choosing the adventurer again brings both back.
    /// </summary>
    [UnityTest]
    public IEnumerator AClientsLook_ReachesTheHostsCopy()
    {
        yield return Host();
        yield return Join();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerController clientOwned = client.LocalPlayer.Current.Controller;
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(gameplay.Resolve<PlayerRegistry>(), client.Manager.LocalClientId);
        CharacterVisuals ownVisuals = clientOwned.GetComponentInChildren<CharacterVisuals>();
        CharacterVisuals copyVisuals = hostCopy.GetComponentInChildren<CharacterVisuals>();
        CharacterAnimationSet adventurerSet = SetOf(clientOwned, Adventurer);
        CharacterAnimationSet banditSet = SetOf(clientOwned, Bandit);
        Assert.That(copyVisuals.AnimationSet, Is.EqualTo(adventurerSet));

        client.Resolve<UserSettings>().SetCharacterLook(Bandit);
        Assert.That(ownVisuals.AnimationSet, Is.EqualTo(banditSet), "The client's own player changes at once.");
        yield return SceneBootTestHelper.WaitUntil(() => copyVisuals.AnimationSet == banditSet, "the host's copy to draw the client's Bandit");

        client.Resolve<UserSettings>().SetCharacterLook(Adventurer);
        yield return SceneBootTestHelper.WaitUntil(() => copyVisuals.AnimationSet == adventurerSet, "the host's copy to draw the adventurer again");
        Assert.That(ownVisuals.AnimationSet, Is.EqualTo(adventurerSet));
    }

    /// <summary>
    /// The host chooses the Bandit before the client joins: the client's copy of the host's player draws the Bandit
    /// from its spawn, and the host's attack plays the Bandit's slash on it.
    /// </summary>
    [UnityTest]
    public IEnumerator ALateJoiner_SeesTheHostsLook()
    {
        yield return Host();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerController hostOwned = gameplay.Resolve<LocalPlayerTracker>().Current.Controller;
        CharacterAnimationSet banditSet = SetOf(hostOwned, Bandit);
        SceneBootTestHelper.ResolveFromMain<UserSettings>().SetCharacterLook(Bandit);
        Assert.That(hostOwned.GetComponentInChildren<CharacterVisuals>().AnimationSet, Is.EqualTo(banditSet));

        yield return Join();
        PlayerController clientCopy = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId);
        CharacterVisuals copyVisuals = clientCopy.GetComponentInChildren<CharacterVisuals>();
        Assert.That(copyVisuals.AnimationSet, Is.EqualTo(banditSet), "The joiner's copy draws the Bandit from its spawn.");

        PlayerWeaponController hostWeapon = hostOwned.GetComponent<PlayerWeaponController>();
        Assert.That(hostWeapon.TryAttack(Vector2.right), Is.True);
        CharacterAnimator copyAnimation = copyVisuals.Animator;
        yield return SceneBootTestHelper.WaitUntil(() => copyAnimation.IsActing, "the host's attack to play on the client's copy");
        Assert.That(copyAnimation.Clip, Is.EqualTo(banditSet.Find(hostWeapon.CurrentWeapon.AttackClip)), "The copy plays the Bandit's slash.");
    }

    /// <summary>
    /// The client's own player draws a red tunic as soon as the client's settings pick it, and the host's copy of it
    /// follows, drawn in a palette of the same colours; going back to the tunic's own colour brings both back to the
    /// look's own colours.
    /// </summary>
    [UnityTest]
    public IEnumerator AClientsColours_ReachTheHostsCopy()
    {
        yield return Host();
        yield return Join();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerController clientOwned = client.LocalPlayer.Current.Controller;
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(gameplay.Resolve<PlayerRegistry>(), client.Manager.LocalClientId);
        PlayerLook ownLook = clientOwned.GetComponent<PlayerLook>();
        PlayerLook copyLook = hostCopy.GetComponent<PlayerLook>();
        CharacterPalette ownPalette = clientOwned.GetComponentInChildren<CharacterPalette>();
        CharacterPalette copyPalette = hostCopy.GetComponentInChildren<CharacterPalette>();
        CharacterColours colours = ownLook.Colours;
        CharacterColourPicks red = colours.Pick(default, colours.IndexOf("Tunic"), "Red");
        Assert.That(copyLook.ShownColours.IsDefault, Is.True);
        Assert.That(copyPalette.IsShown, Is.False);

        client.Resolve<UserSettings>().SetCharacterColour("Tunic", "Red");
        Assert.That(ownLook.ShownColours, Is.EqualTo(red), "The client's own player changes at once.");
        Assert.That(ownPalette.IsShown, Is.True);
        yield return SceneBootTestHelper.WaitUntil(() => copyLook.ShownColours.Equals(red), "the host's copy to draw the client's red tunic");
        Assert.That(copyPalette.IsShown, Is.True);
        Assert.That(copyPalette.Palette.GetPixels32(), Is.EqualTo(ownPalette.Palette.GetPixels32()));

        client.Resolve<UserSettings>().SetCharacterColour("Tunic", "");
        yield return SceneBootTestHelper.WaitUntil(() => copyLook.ShownColours.IsDefault, "the host's copy to draw the look's own colours again");
        Assert.That(copyPalette.IsShown, Is.False);
        Assert.That(ownPalette.IsShown, Is.False);
    }

    /// <summary>
    /// The host picks blonde hair before the client joins: the client's copy of the host's player draws it from its
    /// spawn.
    /// </summary>
    [UnityTest]
    public IEnumerator ALateJoiner_SeesTheHostsColours()
    {
        yield return Host();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerController hostOwned = gameplay.Resolve<LocalPlayerTracker>().Current.Controller;
        PlayerLook hostLook = hostOwned.GetComponent<PlayerLook>();
        CharacterColours colours = hostLook.Colours;
        CharacterColourPicks blonde = colours.Pick(default, colours.IndexOf("Hair"), "Blonde");
        SceneBootTestHelper.ResolveFromMain<UserSettings>().SetCharacterColour("Hair", "Blonde");
        Assert.That(hostLook.ShownColours, Is.EqualTo(blonde));

        yield return Join();
        PlayerController clientCopy = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId);
        Assert.That(clientCopy.GetComponent<PlayerLook>().ShownColours, Is.EqualTo(blonde), "The joiner's copy draws the blonde hair from its spawn.");
        Assert.That(clientCopy.GetComponentInChildren<CharacterPalette>().IsShown, Is.True);
    }

    /// <summary>
    /// The set of the look named <paramref name="lookName"/> in <paramref name="player"/>'s catalog.
    /// </summary>
    private static CharacterAnimationSet SetOf(PlayerController player, string lookName)
    {
        CharacterLookCatalog looks = player.GetComponent<PlayerLook>().Catalog;
        return looks.Get(looks.IndexOf(lookName)).AnimationSet;
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
    /// Starts the in-process client and waits until both sides see both players.
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
        client.DisableColliders();
    }
}
