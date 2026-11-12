using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// A copy of the main menu scene's canvas for the main menu's EditMode tests (Docs/UiRevamp.md, Phase 9): cloned from
/// <c>MainMenu.unity</c> into a plain parent of a chosen size in UI px (its canvas components stripped, so the parent
/// sizes it), with its <see cref="MainMenuController"/> and a <see cref="PlayTogetherController"/> built over a
/// <see cref="MenuTestRig"/>. <see cref="Start"/> runs the controller's Awake and Start, which edit mode skips.
/// </summary>
internal sealed class MainMenuTestScreen : IDisposable
{
    public const string ScenePath = "Assets/Scenes/MainMenu.unity";

    /// <summary>
    /// Clones the scene's canvas into a <paramref name="width"/> x <paramref name="height"/> UI px parent and builds the
    /// controllers over <paramref name="rig"/>, leaving <paramref name="notice"/> for the menu.
    /// </summary>
    public MainMenuTestScreen(MenuTestRig rig, int width, int height, MenuNotice notice = null)
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        GameObject source = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "MainMenuCanvas")
            {
                source = root;
            }
        }

        Parent = rig.Track(new GameObject("Canvas", typeof(RectTransform)));
        ((RectTransform)Parent.transform).sizeDelta = new Vector2(width, height);
        Root = Object.Instantiate(source, Parent.transform, false);
        EditorSceneManager.CloseScene(scene, true);
        Object.DestroyImmediate(Root.GetComponent<UiPixelScale>());
        Object.DestroyImmediate(Root.GetComponent<GraphicRaycaster>());
        Object.DestroyImmediate(Root.GetComponent<CanvasScaler>());
        Object.DestroyImmediate(Root.GetComponent<Canvas>());
        RectTransform rect = (RectTransform)Root.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        Notice = notice ?? new MenuNotice();
        Menu = Root.GetComponent<MainMenuController>();
        Together = Root.GetComponentInChildren<PlayTogetherView>(true);
        PlayTogether = rig.CreatePlayTogether(Together);
        Menu.Construct(rig.Starter, PlayTogether, rig.Line, Notice, null, null, rig.Selection, rig.Menus);
    }

    public GameObject Parent { get; }
    public GameObject Root { get; }
    public MainMenuController Menu { get; }
    public PlayTogetherView Together { get; }
    public PlayTogetherController PlayTogether { get; }
    public MenuNotice Notice { get; }
    public RectTransform Area => (RectTransform)Root.transform;

    /// <summary>
    /// Runs the menu's Awake and Start, as play would.
    /// </summary>
    public void Start()
    {
        Invoke(Menu, "Awake");
        Invoke(Menu, "Start");
    }

    /// <summary>
    /// Disposes Play together; the rig destroys the objects.
    /// </summary>
    public void Dispose()
    {
        PlayTogether.Dispose();
    }

    /// <summary>
    /// <paramref name="rect"/>'s rectangle in UI px from the screen's top left, y going down.
    /// </summary>
    public Rect RectOf(RectTransform rect)
    {
        RectTransform root = Area;
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Rect area = root.rect;
        Vector2 bottomLeft = root.InverseTransformPoint(corners[0]);
        Vector2 topRight = root.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(bottomLeft.x - area.xMin, area.yMax - topRight.y, topRight.x - area.xMin, area.yMax - bottomLeft.y);
    }

    /// <summary>
    /// Calls <paramref name="behaviour"/>'s method <paramref name="method"/>.
    /// </summary>
    private static void Invoke(MonoBehaviour behaviour, string method)
    {
        behaviour.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(behaviour, null);
    }
}
