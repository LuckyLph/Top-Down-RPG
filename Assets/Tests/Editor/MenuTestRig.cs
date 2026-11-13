using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

/// <summary>
/// What the main menu's tests share (Docs/UiRevamp.md, Phase 9): a listed starting world, Home, with two named entries;
/// a fake game flow and network connection; a save folder, the session's saved state and where players start; user
/// settings; the joining line; the <see cref="MenuSessionStarter"/> and <see cref="ContinueLine"/> over them; fake
/// interfaces, clipboard and wall clock; the UI scale on a 1920 x 1080 display; and a menu stack and selection over an
/// event system of its own. It builds the Play together, Saved games (Phase 10) and Welcome (Phase 11) controllers over
/// the views a test gives it. The first run is over unless a test asks for it.
/// </summary>
internal sealed class MenuTestRig : IDisposable
{
    public static readonly Vector2 NearNorth = new(40f, 50f);

    private readonly List<Object> created = new();

    /// <summary>
    /// Builds every part, the joining line started, the first run over unless <paramref name="firstRunDone"/> is false.
    /// </summary>
    public MenuTestRig(bool firstRunDone = true)
    {
        Home = Track(WorldDefinition.Create(
            "Home",
            new Vector2Int(2048, 0),
            Vector2Int.one,
            new[] { Vector2Int.zero },
            new WorldDefinition.Entry("start", new Vector2(10.5f, 12.5f), true, "Start camp"),
            new WorldDefinition.Entry("north", new Vector2(44f, 54f), false, "North road")));
        Scenes = Track(ScriptableObject.CreateInstance<GameScenes>());
        SerializedObject data = new(Scenes);
        SerializedProperty listed = data.FindProperty("worlds");
        listed.arraySize = 1;
        listed.GetArrayElementAtIndex(0).objectReferenceValue = Home;
        data.FindProperty("startingWorld").objectReferenceValue = Home;
        data.FindProperty("startingWorldEntry").stringValue = "start";
        data.ApplyModifiedPropertiesWithoutUndo();
        Network = Track(ScriptableObject.CreateInstance<NetworkSettings>());
        Flow = new FakeGameFlow { Scenes = Scenes };
        Settings = new UserSettings(new MemorySaveStore());
        if (firstRunDone)
        {
            Settings.SetFirstRunDone();
        }

        Scale = new UiScale(AssetDatabase.LoadAssetAtPath<UiScaleSettings>("Assets/Data/UI/UiScale.asset"), Settings);
        Scale.Apply(1920, 1080, Settings.UiSizeStep);
        Connection = new FakeSessionConnection(Network) { LocalPlayerId = Settings.PlayerId };
        Folder = new MemorySaveFolder();
        WallClock = new ManualWallClock();
        Save = new GameSave(Folder, WallClock);
        SessionSave = new SessionSave(new ManualClock());
        Starts = new PlayerStarts(Scenes, SessionSave);
        Loading = new LoadingMessage(Flow);
        Loading.Start();
        Starter = new MenuSessionStarter(Flow, Connection, Save, SessionSave, Starts, Settings, Loading);
        Line = new ContinueLine(Save, Scenes, Settings, WallClock);
        Interfaces = new FakeNetworkInterfaces();
        Clipboard = new FakeClipboard();
        Hud = Track(HudSettings.Create(messageSeconds: 1.2f));
        MenuInput = new FakeMenuInput();
        Menus = new MenuStack(MenuInput);
        EventObject = Track(new GameObject("EventSystem", typeof(EventSystem)));
        Selection = new UISelection(EventObject.GetComponent<EventSystem>());
    }

    public WorldDefinition Home { get; }
    public GameScenes Scenes { get; }
    public NetworkSettings Network { get; }
    public FakeGameFlow Flow { get; }
    public FakeSessionConnection Connection { get; }
    public MemorySaveFolder Folder { get; }
    public ManualWallClock WallClock { get; }
    public GameSave Save { get; }
    public SessionSave SessionSave { get; }
    public PlayerStarts Starts { get; }
    public UserSettings Settings { get; }
    public UiScale Scale { get; }
    public LoadingMessage Loading { get; }
    public MenuSessionStarter Starter { get; }
    public ContinueLine Line { get; }
    public FakeNetworkInterfaces Interfaces { get; }
    public FakeClipboard Clipboard { get; }
    public HudSettings Hud { get; }
    public FakeMenuInput MenuInput { get; }
    public MenuStack Menus { get; }
    public GameObject EventObject { get; }
    public UISelection Selection { get; }

    /// <summary>
    /// Keeps <paramref name="instance"/> to destroy on <see cref="Dispose"/> and returns it.
    /// </summary>
    public T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }

    /// <summary>
    /// Writes a session's save holding the local player at <paramref name="local"/> in Home, played for
    /// <paramref name="seconds"/>, under <paramref name="session"/>; returns its file's name.
    /// </summary>
    public string WriteSave(Vector2 local, double seconds, string session = "saved-session")
    {
        GameSaveData data = new(seconds, new[] { new GameSaveData.Player(Connection.LocalPlayerId, Home.WorldId, Home.ToWorld(local)) }, new GameSaveData.World[0]);
        data.SetSession(session);
        Save.Write(data);
        return GameSave.FileNameOf(data);
    }

    /// <summary>
    /// Writes a joined client's copy of the local player's place at <paramref name="local"/> in Home in the host's
    /// session <paramref name="hostSession"/>, played for <paramref name="seconds"/>, its host there or gone as
    /// <paramref name="hostGone"/> says; returns its file's name.
    /// </summary>
    public string WriteCopy(Vector2 local, double seconds, string hostSession, bool hostGone)
    {
        GameSaveData data = new(seconds, new[] { new GameSaveData.Player(Connection.LocalPlayerId, Home.WorldId, Home.ToWorld(local)) }, new GameSaveData.World[0]);
        data.SetHost(hostSession, hostGone);
        Save.Write(data);
        return GameSave.FileNameOf(data);
    }

    /// <summary>
    /// Adds a home network and a Tailscale address to the interfaces.
    /// </summary>
    public void AddAddresses()
    {
        Interfaces.Add("Wi-Fi", true, "192.168.1.20");
        Interfaces.Add("Tailscale", false, "100.101.102.103");
    }

    /// <summary>
    /// A Play together controller over <paramref name="view"/> and the rig's parts, started, hosting another saved game
    /// through <paramref name="savedGames"/> (none for null).
    /// </summary>
    public PlayTogetherController CreatePlayTogether(PlayTogetherView view, SavedGamesController savedGames = null)
    {
        PlayTogetherController controller = new(view, Starter, Line, new HostAddresses(Interfaces, 7777), Clipboard, WallClock, Hud, Settings, Menus, Selection, savedGames);
        controller.Start();
        return controller;
    }

    /// <summary>
    /// A Welcome controller over <paramref name="view"/> and the rig's parts, started.
    /// </summary>
    public WelcomeController CreateWelcome(WelcomeView view)
    {
        WelcomeController controller = new(view, Settings, Scale, Menus, Selection);
        controller.Start();
        return controller;
    }

    /// <summary>
    /// A Saved games controller over <paramref name="view"/> and the rig's parts, started.
    /// </summary>
    public SavedGamesController CreateSavedGames(SavedGamesView view)
    {
        SavedGamesController controller = new(view, Starter, Save, Line, Menus, Selection);
        controller.Start();
        return controller;
    }

    /// <summary>
    /// Stops the joining line and destroys every object the rig made.
    /// </summary>
    public void Dispose()
    {
        Loading.Dispose();
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] != null)
            {
                Object.DestroyImmediate(created[i]);
            }
        }

        created.Clear();
    }
}
