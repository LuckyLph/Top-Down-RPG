using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using VContainer;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// PlayMode tests for Play together (Docs/UiRevamp.md, Phase 9) in the booted main menu: joining an in-process host
/// through the screen's field and Join, with the connecting dialog, the joining line over the fader until the fade in
/// and the host remembered, then the host-gone dialog once it leaves; and cancelling while connecting.
/// </summary>
public class PlayTogetherPlayModeTests
{
    private const string PlayerPrefabPath = "Assets/Prefabs/Player/Player.prefab";
    private const string CombatSettingsPath = "Assets/Data/Combat/CombatSettings.asset";
    private const string GameplaySettingsPath = "Assets/Data/GameplaySettings.asset";

    private InProcessHost remoteHost;

    /// <summary>
    /// Shuts down and destroys the remote host when the test started one.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        remoteHost?.Dispose();
        remoteHost = null;
    }

    /// <summary>
    /// Typing the host's address and pressing Join shows the connecting dialog; once connected the joining line names
    /// the host over the fader, the joined game loads the world the host put the player in, the line goes as it fades in,
    /// and the host is first among the recent hosts. When the host leaves, the menu shows the host-gone dialog.
    /// </summary>
    [UnityTest]
    public IEnumerator JoiningThroughPlayTogether_ShowsTheDialogAndTheJoiningLine_AndRemembersTheHost()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu(new MemorySaveFolder());
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        LoadingMessage loading = SceneBootTestHelper.ResolveFromMain<LoadingMessage>();
        UserSettings settings = SceneBootTestHelper.ResolveFromMain<UserSettings>();
        WorldDefinition testlands = gameFlow.Scenes.StartingWorld;
        remoteHost = StartRemoteHost(session, gameFlow.Scenes);
        remoteHost.SpawnWorld = testlands;
        remoteHost.SpawnPosition = testlands.ToWorld(testlands.FindEntry("start").Position);

        MainMenuController menu = Object.FindAnyObjectByType<MainMenuController>();
        menu.OpenPlayTogether();
        yield return null;
        PlayTogetherView view = Object.FindAnyObjectByType<PlayTogetherView>();
        Assert.That(view.IsShown, Is.True);
        view.Field.text = "127.0.0.1";
        view.JoinButton.onClick.Invoke();
        Assert.That(view.Dialog.IsShown, Is.True);
        Assert.That(view.Dialog.Message, Does.StartWith($"Connecting to 127.0.0.1:{session.Settings.Port}…"));

        LoadingLine line = Object.FindAnyObjectByType<LoadingLine>(FindObjectsInactive.Include);
        Assert.That(line, Is.Not.Null, "Main holds the loading line.");
        yield return SceneBootTestHelper.WaitUntil(() => loading.Text.Length > 0, "the joining line once connected");
        Assert.That(loading.Text, Is.EqualTo("Joining 127.0.0.1's game…"));
        Assert.That(LineText(line).gameObject.activeInHierarchy, Is.True);
        Assert.That(LineText(line).text, Is.EqualTo(loading.Text));
        Assert.That(settings.RecentHosts[0], Is.EqualTo("127.0.0.1"), "The host joined goes first among the recent hosts.");

        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsInGame && !gameFlow.IsTransitioning, "the client to load the world it was placed in");
        Assert.That(loading.Text, Is.Empty, "The joining line goes as the game fades in.");
        Assert.That(LineText(line).gameObject.activeInHierarchy, Is.False);
        Assert.That(session.IsConnectedClient, Is.True);

        remoteHost.Manager.Shutdown();
        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsInMenu && !gameFlow.IsTransitioning, "the client to return to the menu after the host left");
        MainMenuController back = null;
        yield return SceneBootTestHelper.WaitUntil(() => (back = Object.FindAnyObjectByType<MainMenuController>()) != null && back.Dialog.IsShown, "the menu's host-gone dialog");
        Assert.That(back.Dialog.Message, Is.EqualTo(DialogNoticeView.BySentence(MainMenuController.HostGoneMessage)));
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(back.Dialog.PrimaryButton.gameObject), "Continue alone is selected.");
    }

    /// <summary>
    /// Cancelling while connecting to an address no host answers ends the attempt at once: the client shuts down, the
    /// dialog hides, nothing is said or remembered and Join is selected, in the menu.
    /// </summary>
    [UnityTest]
    public IEnumerator CancellingWhileConnecting_EndsTheJoin()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu(new MemorySaveFolder());
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        UserSettings settings = SceneBootTestHelper.ResolveFromMain<UserSettings>();
        MainMenuController menu = Object.FindAnyObjectByType<MainMenuController>();
        PlayTogetherController controller = Object.FindAnyObjectByType<MenuLifetimeScope>().Container.Resolve<PlayTogetherController>();
        menu.OpenPlayTogether();
        yield return null;
        PlayTogetherView view = Object.FindAnyObjectByType<PlayTogetherView>();
        view.Field.text = "127.0.0.1";

        view.JoinButton.onClick.Invoke();
        yield return null;
        yield return null;
        Assert.That(controller.IsConnecting, Is.True);
        Assert.That(session.IsActive, Is.True, "The client is trying to connect.");
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(view.Dialog.SecondaryButton.gameObject), "Cancel is selected.");

        view.Dialog.SecondaryButton.onClick.Invoke();
        yield return SceneBootTestHelper.WaitUntil(() => !controller.IsConnecting && !session.IsActive, "the join to end");
        Assert.That(view.Dialog.IsShown, Is.False);
        Assert.That(view.Notice.text, Is.Empty);
        Assert.That(settings.RecentHosts, Is.Empty);
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(view.JoinButton.gameObject));
        Assert.That(gameFlow.IsInMenu && !gameFlow.IsTransitioning, Is.True);
    }

    /// <summary>
    /// The loading line's text.
    /// </summary>
    private static TMPro.TMP_Text LineText(LoadingLine line)
    {
        return line.GetComponentInChildren<TMPro.TMP_Text>(true);
    }

    /// <summary>
    /// Starts an in-process host on the session's port that spawns joined clients' players, built from the project's
    /// player prefab, combat and gameplay settings and <paramref name="gameScenes"/>.
    /// </summary>
    private static InProcessHost StartRemoteHost(NetworkSession session, GameScenes gameScenes)
    {
#if UNITY_EDITOR
        NetworkObject playerPrefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(PlayerPrefabPath);
        CombatSettings combatSettings = AssetDatabase.LoadAssetAtPath<CombatSettings>(CombatSettingsPath);
        GameplaySettings gameplaySettings = AssetDatabase.LoadAssetAtPath<GameplaySettings>(GameplaySettingsPath);
        return InProcessHost.Start(session, playerPrefab, gameScenes, combatSettings, gameplaySettings);
#else
        Assert.Ignore("The in-process host loads the player prefab through the AssetDatabase.");
        return null;
#endif
    }
}
