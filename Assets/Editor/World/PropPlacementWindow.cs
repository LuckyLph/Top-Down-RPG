using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor window for placing props in the open area scene. It shows a palette of the prop prefabs; while one is picked,
/// each click in the Scene view places it with its root snapped by <see cref="PropGridSnapping"/>, under the scene's
/// <c>Props</c> object, with undo. A preview shows the sprite and, for a blocking prop, the cells it will block, in
/// red when another obstacle (a prop or a wall) already covers them, where placing is refused. Escape stops placing.
/// The same snapping is available for props already in a scene through Snap Selected Props To Grid.
/// </summary>
public sealed class PropPlacementWindow : EditorWindow
{
    private const string PropsFolder = "Assets/Prefabs/Props";
    private const string PropsRootName = "Props";
    private const string VisualsName = "Visuals";
    private const string MenuPath = ProjectScenes.MenuRoot + "Props/";
    private const string AllCategories = "All";
    private const float TileSize = 76f;
    private const int PreviewSortingOrder = short.MaxValue;

    private static readonly Color FreeFill = new(0.3f, 1f, 0.4f, 0.2f);
    private static readonly Color FreeOutline = new(0.3f, 1f, 0.4f, 0.9f);
    private static readonly Color BlockedFill = new(1f, 0.3f, 0.3f, 0.3f);
    private static readonly Color BlockedOutline = new(1f, 0.3f, 0.3f, 0.9f);
    private static readonly Color FootprintOutline = new(1f, 1f, 1f, 0.9f);
    private static readonly Color PreviewTint = new(1f, 1f, 1f, 0.6f);

    private readonly List<GameObject> prefabs = new();
    private readonly List<string> categories = new();
    private readonly Collider2D[] overlapHits = new Collider2D[4];
    private string search = string.Empty;
    private int categoryIndex;
    private Vector2 scroll;
    private GameObject picked;
    private SpriteRenderer preview;
    private NavigationGrid2D cachedGrid;

    /// <summary>
    /// Opens the prop placement window.
    /// </summary>
    [MenuItem(MenuPath + "Prop Placement", priority = 60)]
    private static void Open()
    {
        GetWindow<PropPlacementWindow>("Props");
    }

    /// <summary>
    /// Snaps every selected prop to its area's grid, with undo.
    /// </summary>
    [MenuItem(MenuPath + "Snap Selected Props To Grid", priority = 61)]
    private static void SnapSelectedMenu()
    {
        SnapSelected();
    }

    /// <summary>
    /// Offers Snap Selected Props To Grid only while something is selected.
    /// </summary>
    [MenuItem(MenuPath + "Snap Selected Props To Grid", true)]
    private static bool CanSnapSelected()
    {
        return Selection.gameObjects.Length > 0;
    }

    /// <summary>
    /// Loads the palette and starts following the Scene view, hierarchy and project changes.
    /// </summary>
    private void OnEnable()
    {
        LoadPrefabs();
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.hierarchyChanged += ForgetGrid;
        EditorApplication.projectChanged += LoadPrefabs;
    }

    /// <summary>
    /// Stops placing and stops following the Scene view, hierarchy and project changes.
    /// </summary>
    private void OnDisable()
    {
        StopPlacing();
        SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.hierarchyChanged -= ForgetGrid;
        EditorApplication.projectChanged -= LoadPrefabs;
    }

    /// <summary>
    /// Draws the target area, the filters, the palette and the snap button.
    /// </summary>
    private void OnGUI()
    {
        NavigationGrid2D grid = AreaGrid();
        if (grid == null)
        {
            EditorGUILayout.HelpBox("Open an area scene: props are placed in the scene that holds a NavigationGrid2D.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.LabelField("Area", grid.gameObject.scene.name);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            categoryIndex = EditorGUILayout.Popup(categoryIndex, categories.ToArray(), GUILayout.Width(110f));
        }

        if (picked != null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox($"Placing {picked.name}: click in the Scene view. Escape stops.", MessageType.None);
                if (GUILayout.Button("Stop", GUILayout.Width(50f), GUILayout.Height(38f)))
                {
                    StopPlacing();
                }
            }
        }

        DrawPalette();

        if (GUILayout.Button("Snap Selected Props To Grid"))
        {
            SnapSelected();
        }

        if (AssetPreview.IsLoadingAssetPreviews())
        {
            Repaint();
        }
    }

    /// <summary>
    /// Draws the prefabs that match the search and category as a wrapping grid of thumbnail buttons; clicking one picks
    /// it for placing, clicking the picked one again stops.
    /// </summary>
    private void DrawPalette()
    {
        string category = categoryIndex > 0 && categoryIndex < categories.Count ? categories[categoryIndex] : null;
        int perRow = Mathf.Max(1, Mathf.FloorToInt((position.width - 20f) / (TileSize + 4f)));
        scroll = EditorGUILayout.BeginScrollView(scroll);
        int shown = 0;
        EditorGUILayout.BeginHorizontal();
        foreach (GameObject prefab in prefabs)
        {
            if (prefab == null || (category != null && Category(prefab) != category)
                || (search.Length > 0 && prefab.name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0))
            {
                continue;
            }

            if (shown > 0 && shown % perRow == 0)
            {
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
            }

            Sprite sprite = SpriteOf(prefab);
            Texture thumbnail = sprite != null ? AssetPreview.GetAssetPreview(sprite) : null;
            GUIContent content = thumbnail != null ? new GUIContent(thumbnail, prefab.name) : new GUIContent(prefab.name, prefab.name);
            bool isPicked = prefab == picked;
            bool clicked = GUILayout.Toggle(isPicked, content, GUI.skin.button, GUILayout.Width(TileSize), GUILayout.Height(TileSize));
            if (clicked != isPicked)
            {
                if (clicked)
                {
                    Pick(prefab);
                }
                else
                {
                    StopPlacing();
                }
            }

            shown++;
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// While a prop is picked, keeps the Scene view's clicks for placing, moves the preview to the snapped position
    /// under the pointer, draws the cells it would block, and places it on a left click unless those cells are taken.
    /// Escape stops placing.
    /// </summary>
    private void OnSceneGUI(SceneView view)
    {
        if (picked == null)
        {
            return;
        }

        Event current = Event.current;
        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
        {
            StopPlacing();
            current.Use();
            Repaint();
            return;
        }

        NavigationGrid2D navigation = AreaGrid();
        GridLayout grid = navigation != null ? navigation.GetComponentInParent<GridLayout>() : null;
        if (grid == null)
        {
            HidePreview();
            return;
        }

        if (current.type == EventType.Layout)
        {
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        }

        view.wantsMouseMove = true;
        Vector2 pointer = HandleUtility.GUIPointToWorldRay(current.mousePosition).origin;
        Vector3 root = PropGridSnapping.Snap(grid, pointer, picked);
        bool hasFootprint = PropGridSnapping.TryGetFootprint(picked, out Rect footprint);
        bool taken = hasFootprint && IsTaken(root, footprint, navigation.ObstacleLayers);
        ShowPreview(root);

        switch (current.type)
        {
            case EventType.Repaint:
                DrawFootprint(grid, root, footprint, hasFootprint, taken);
                break;
            case EventType.MouseMove:
                view.Repaint();
                break;
            case EventType.MouseDown when current.button == 0 && !current.alt:
                if (!taken)
                {
                    Place(navigation, root);
                }

                current.Use();
                break;
        }
    }

    /// <summary>
    /// Instantiates the picked prefab at <paramref name="root"/> under the area scene's <c>Props</c> object as one undo
    /// step (creating that object too when needed), and has the navigation grid's gizmos rebuild so the new obstacle
    /// shows.
    /// </summary>
    private void Place(NavigationGrid2D navigation, Vector3 root)
    {
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName($"Place {picked.name}");
        int undoGroup = Undo.GetCurrentGroup();
        Scene scene = navigation.gameObject.scene;
        Transform parent = PropsRoot(scene);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(picked, scene);
        Undo.RegisterCreatedObjectUndo(instance, $"Place {picked.name}");
        instance.transform.SetParent(parent, false);
        instance.transform.position = root;
        Undo.CollapseUndoOperations(undoGroup);
        navigation.MarkGizmoGridStale();
        EditorSceneManager.MarkSceneDirty(scene);
    }

    /// <summary>
    /// Snaps the root of every selected prop instance (or of the prop a selected child belongs to) to its scene's area
    /// grid with one undo step, and logs how many moved.
    /// </summary>
    private static void SnapSelected()
    {
        int moved = 0;
        foreach (GameObject selected in Selection.gameObjects)
        {
            GameObject prop = PropInstanceRoot(selected);
            NavigationGrid2D navigation = prop != null ? FindGrid(prop.scene) : null;
            GridLayout grid = navigation != null ? navigation.GetComponentInParent<GridLayout>() : null;
            if (grid == null)
            {
                continue;
            }

            Vector3 snapped = PropGridSnapping.Snap(grid, prop.transform.position, prop);
            if (snapped == prop.transform.position)
            {
                continue;
            }

            Undo.RecordObject(prop.transform, "Snap Props To Grid");
            prop.transform.position = snapped;
            navigation.MarkGizmoGridStale();
            moved++;
        }

        Debug.Log($"Snapped {moved} prop(s) to the grid.");
    }

    /// <summary>
    /// The outermost prefab instance <paramref name="selected"/> belongs to when it is a prop from the props folder,
    /// otherwise null.
    /// </summary>
    private static GameObject PropInstanceRoot(GameObject selected)
    {
        GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(selected);
        if (root == null)
        {
            return null;
        }

        string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
        return path.StartsWith(PropsFolder + "/") ? root : null;
    }

    /// <summary>
    /// Picks <paramref name="prefab"/> for placing and creates its preview.
    /// </summary>
    private void Pick(GameObject prefab)
    {
        StopPlacing();
        picked = prefab;
        GameObject previewObject = new($"{prefab.name} (preview)") { hideFlags = HideFlags.HideAndDontSave };
        preview = previewObject.AddComponent<SpriteRenderer>();
        preview.sprite = SpriteOf(prefab);
        preview.color = PreviewTint;
        preview.sortingLayerName = "Entities";
        preview.sortingOrder = PreviewSortingOrder;
        previewObject.SetActive(false);
        SceneView.RepaintAll();
    }

    /// <summary>
    /// Drops the picked prefab and destroys its preview.
    /// </summary>
    private void StopPlacing()
    {
        picked = null;
        if (preview != null)
        {
            DestroyImmediate(preview.gameObject);
        }

        preview = null;
        SceneView.RepaintAll();
    }

    /// <summary>
    /// Shows the preview sprite where the picked prop's visuals would be with its root at <paramref name="root"/>.
    /// </summary>
    private void ShowPreview(Vector3 root)
    {
        if (preview == null)
        {
            return;
        }

        Transform visuals = picked.transform.Find(VisualsName);
        preview.transform.position = root + (visuals != null ? visuals.localPosition : Vector3.zero);
        preview.gameObject.SetActive(true);
    }

    /// <summary>
    /// Hides the preview while there is no grid to place on.
    /// </summary>
    private void HidePreview()
    {
        if (preview != null)
        {
            preview.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Whether a collider on <paramref name="obstacleLayers"/> (the Obstacles layer when the grid has none set) already
    /// lies in the footprint at <paramref name="root"/>, shrunk by the grid's edge inset.
    /// </summary>
    private bool IsTaken(Vector3 root, Rect footprint, LayerMask obstacleLayers)
    {
        ContactFilter2D filter = new() { useTriggers = false };
        filter.SetLayerMask(obstacleLayers.value != 0 ? obstacleLayers : LayerMask.GetMask("Obstacles"));
        Vector2 size = new(
            Mathf.Max(0.01f, footprint.width - 2f * NavigationGrid2D.ObstacleEdgeInset),
            Mathf.Max(0.01f, footprint.height - 2f * NavigationGrid2D.ObstacleEdgeInset));
        Physics2D.SyncTransforms();
        return Physics2D.OverlapBox((Vector2)root + footprint.center, size, 0f, filter, overlapHits) > 0;
    }

    /// <summary>
    /// Draws the cells the footprint would block (green, or red when taken) and the footprint box itself; a walk-through
    /// prop gets a small cross at its root.
    /// </summary>
    private static void DrawFootprint(GridLayout grid, Vector3 root, Rect footprint, bool hasFootprint, bool taken)
    {
        if (!hasFootprint)
        {
            Handles.color = FreeOutline;
            Handles.DrawLine(root + new Vector3(-0.2f, 0f), root + new Vector3(0.2f, 0f));
            Handles.DrawLine(root + new Vector3(0f, -0.2f), root + new Vector3(0f, 0.2f));
            return;
        }

        BoundsInt cells = PropGridSnapping.BlockedCells(grid, root, footprint);
        Vector3 min = grid.CellToWorld(cells.min);
        Vector3 max = grid.CellToWorld(new Vector3Int(cells.xMax, cells.yMax, cells.zMin));
        Handles.DrawSolidRectangleWithOutline(new Rect(min, max - min), taken ? BlockedFill : FreeFill, taken ? BlockedOutline : FreeOutline);
        Handles.DrawSolidRectangleWithOutline(new Rect((Vector2)root + footprint.min, footprint.size), Color.clear, FootprintOutline);
    }

    /// <summary>
    /// The scene's root object named <c>Props</c>, created at the origin (with undo) when it does not exist yet.
    /// </summary>
    private static Transform PropsRoot(Scene scene)
    {
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            if (rootObject.name == PropsRootName)
            {
                return rootObject.transform;
            }
        }

        GameObject created = new(PropsRootName);
        SceneManager.MoveGameObjectToScene(created, scene);
        Undo.RegisterCreatedObjectUndo(created, "Create Props");
        return created.transform;
    }

    /// <summary>
    /// The navigation grid props are placed against: the one in the active scene, else the first open scene with one.
    /// Cached until the hierarchy changes.
    /// </summary>
    private NavigationGrid2D AreaGrid()
    {
        if (cachedGrid != null)
        {
            return cachedGrid;
        }

        cachedGrid = FindGrid(SceneManager.GetActiveScene());
        for (int i = 0; cachedGrid == null && i < SceneManager.sceneCount; i++)
        {
            cachedGrid = FindGrid(SceneManager.GetSceneAt(i));
        }

        return cachedGrid;
    }

    /// <summary>
    /// Forgets the cached grid so the next lookup finds it again.
    /// </summary>
    private void ForgetGrid()
    {
        cachedGrid = null;
    }

    /// <summary>
    /// The first <see cref="NavigationGrid2D"/> in the loaded <paramref name="scene"/>, or null.
    /// </summary>
    private static NavigationGrid2D FindGrid(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return null;
        }

        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            NavigationGrid2D grid = rootObject.GetComponentInChildren<NavigationGrid2D>(true);
            if (grid != null)
            {
                return grid;
            }
        }

        return null;
    }

    /// <summary>
    /// Loads every prop prefab in the category folders under the props folder (the two base prefabs at its top are
    /// left out), sorted by category and name, and the category list for the filter.
    /// </summary>
    private void LoadPrefabs()
    {
        prefabs.Clear();
        categories.Clear();
        categories.Add(AllCategories);
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PropsFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetDirectoryName(path).Replace('\\', '/') == PropsFolder)
            {
                continue;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            prefabs.Add(prefab);
            string category = Category(prefab);
            if (!categories.Contains(category))
            {
                categories.Add(category);
            }
        }

        prefabs.Sort((a, b) =>
        {
            int byCategory = string.CompareOrdinal(Category(a), Category(b));
            return byCategory != 0 ? byCategory : string.CompareOrdinal(a.name, b.name);
        });
        categoryIndex = Mathf.Clamp(categoryIndex, 0, categories.Count - 1);
        Repaint();
    }

    /// <summary>
    /// The category of <paramref name="prefab"/>: the name of the folder it sits in.
    /// </summary>
    private static string Category(GameObject prefab)
    {
        return System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(prefab)));
    }

    /// <summary>
    /// The sprite on <paramref name="prefab"/>'s <c>Visuals</c> child, or null.
    /// </summary>
    private static Sprite SpriteOf(GameObject prefab)
    {
        Transform visuals = prefab.transform.Find(VisualsName);
        return visuals != null && visuals.TryGetComponent(out SpriteRenderer renderer) ? renderer.sprite : null;
    }
}
