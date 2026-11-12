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
/// interfaces, clipboard and wall clock; and a menu stack and selection over an event system of its own.
/// </summary>
internal sealed class MenuTestRig : IDisposable
{
    public static readonly Vector2 NearNorth = new(40f, 50f);

    private readonly List<Object> created = new();

    /// <summary>
    /// Builds every part, the joining line started.
    /// </summary>
    public MenuTestRig()
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
    /// <paramref name="seconds"/>.
    /// </summary>
    public void WriteSave(Vector2 local, double seconds)
    {
        GameSaveData data = new(seconds, new[] { new GameSaveData.Player(Connection.LocalPlayerId, Home.WorldId, Home.ToWorld(local)) }, new GameSaveData.World[0]);
        data.SetSession("saved-session");
        Save.Write(data);
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
    /// A Play together controller over <paramref name="view"/> and the rig's parts, started.
    /// </summary>
    public PlayTogetherController CreatePlayTogether(PlayTogetherView view)
    {
        PlayTogetherController controller = new(view, Starter, Line, new HostAddresses(Interfaces, 7777), Clipboard, WallClock, Hud, Settings, Menus, Selection);
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
