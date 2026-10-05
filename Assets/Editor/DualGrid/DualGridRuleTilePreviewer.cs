using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Renders a sample dual-grid tilemap painted with a given rule tile into a texture for the rule tile inspector,
/// using a hidden preview scene that persists across inspector redraws.
/// </summary>
public static class DualGridRuleTilePreviewer
{
    private static Scene previewScene;
    private static Camera previewCamera;
    private static DualGridTilemapModule previewDualGridTilemapModule;

    private static RenderTexture renderTexture;

    /// <summary>
    /// Offset used to spawn preview objects, so they are outside of the active scene view when rendered
    /// <para></para>
    /// This is used because cameras don't work properly in preview scenes. 
    /// The best workaround found was to move the preview objects into the active scene, render the camera and move them back into the preview scene.
    /// <para></para>
    /// Thanks Unity!
    /// </summary>
    private static Vector3 previewObjectsPositionOffset = new(100000, 100000, 0);

    /// <summary>
    /// Loads the preview scene with a specific tile.
    /// <para></para>
    /// The preview scene and objects will be initialized if not already.
    /// </summary>
    public static void LoadPreviewScene(DualGridRuleTile tile)
    {
        if (previewScene == default)
        {
            previewScene = EditorSceneManager.NewPreviewScene();
        }

        if (previewDualGridTilemapModule == null)
        {
            previewDualGridTilemapModule = CreateDualGridTilemapModule(tile);
            EditorSceneManager.MoveGameObjectToScene(previewDualGridTilemapModule.transform.parent.gameObject, previewScene);
        }

        if (previewDualGridTilemapModule.RenderTile != tile)
        {
            UpdateDualGridTile(previewDualGridTilemapModule, tile);
        }

        if (previewCamera == null)
        {
            previewCamera = CreateCamera();
            EditorSceneManager.MoveGameObjectToScene(previewCamera.gameObject, previewScene);
        }

        if (renderTexture == null)
        {
            renderTexture = new RenderTexture(1350, 420, 16, RenderTextureFormat.Default);
        }
    }

    /// <summary>
    /// Forcefully renders the preview dual grid tilemap, by temporarily moving the preview objects (camera and tilemap) into the active scene,
    /// so that they are rendered, and then back into the preview scene, so that they are hidden.
    /// <para></para>
    /// This is only done because temporary preview scenes don't allow cameras to work properly.
    /// </summary>
    public static void UpdateRenderTexture()
    {
        MovePreviewObjectsToScene(EditorSceneManager.GetActiveScene());

        previewCamera.targetTexture = renderTexture;
        previewCamera.Render();
        previewCamera.targetTexture = null;

        MovePreviewObjectsToScene(previewScene);

        static void MovePreviewObjectsToScene(Scene scene)
        {
            EditorSceneManager.MoveGameObjectToScene(previewDualGridTilemapModule.transform.parent.gameObject, scene);
            EditorSceneManager.MoveGameObjectToScene(previewCamera.gameObject, scene);
        }
    }

    /// <summary>
    /// Returns the current tilemap preview render texture.
    /// </summary>
    public static RenderTexture GetRenderTexture()
    {
        if (renderTexture == null)
            Debug.LogError("RenderTexture not initialized. Make sure the preview scene is loaded.");

        return renderTexture;
    }

    /// <summary>
    /// Creates a new dual-grid tilemap far from the origin, assigns <paramref name="dualGridRuleTile"/> and paints the
    /// sample layout on it.
    /// </summary>
    private static DualGridTilemapModule CreateDualGridTilemapModule(DualGridRuleTile dualGridRuleTile)
    {
        var dualGridTilemapModule = DualGridTilemapModuleEditor.CreateNewDualGridTilemap();

        dualGridTilemapModule.transform.parent.position += previewObjectsPositionOffset;
        UpdateDualGridTile(dualGridTilemapModule, dualGridRuleTile);
        PaintSampleTiles(dualGridTilemapModule);

        return dualGridTilemapModule;
    }

    /// <summary>
    /// Creates the orthographic preview camera framed on the sample layout, with a gray background.
    /// </summary>
    private static Camera CreateCamera()
    {
        var cameraObject = new GameObject("PreviewCamera");

        Camera camera = cameraObject.AddComponent<Camera>();

        camera.orthographic = true;
        camera.transform.position = new Vector3(0, -5.5f, -10) + previewObjectsPositionOffset;
        camera.orthographicSize = 3;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 15f;
        camera.backgroundColor = Color.gray;
        camera.cullingMask = -1;

        return camera;
    }

    /// <summary>
    /// Clears the data tilemap and paints a fixed set of sample shapes that exercise the rule tile's corner and edge
    /// configurations.
    /// </summary>
    private static void PaintSampleTiles(DualGridTilemapModule module)
    {
        var tile = ScriptableObject.CreateInstance<Tile>();

        module.DataTilemap.ClearAllTiles();

        SetTile(-9, -4);
        SetTile(-7, -4);

        SetTile(-9, -6);
        SetTile(-9, -7);
        SetTile(-9, -8);
        SetTile(-8, -6);
        SetTile(-8, -8);
        SetTile(-7, -6);
        SetTile(-7, -7);
        SetTile(-7, -8);

        SetTile(-5, -4);
        SetTile(-4, -4);
        SetTile(-3, -4);

        SetTile(-5, -6);
        SetTile(-4, -6);
        SetTile(-3, -6);
        SetTile(-5, -7);
        SetTile(-4, -7);
        SetTile(-3, -7);
        SetTile(-5, -8);
        SetTile(-4, -8);
        SetTile(-3, -8);

        SetTile(-1, -4);
        SetTile(-1, -5);
        SetTile(-1, -6);
        SetTile(-1, -8);

        SetTile(2, -4);
        SetTile(1, -5);
        SetTile(2, -5);
        SetTile(3, -5);
        SetTile(2, -6);

        SetTile(1, -8);
        SetTile(2, -8);
        SetTile(3, -8);

        SetTile(5, -4);
        SetTile(5, -5);
        SetTile(6, -5);
        SetTile(7, -4);
        SetTile(8, -4);
        SetTile(8, -5);

        SetTile(5, -7);
        SetTile(5, -8);
        SetTile(6, -7);
        SetTile(8, -7);
        SetTile(7, -8);
        SetTile(8, -8);

        void SetTile(int x, int y)
        {
            module.DataTilemap.SetTile(new Vector3Int(x, y, 0), tile);
        }
    }

    /// <summary>
    /// Switches the preview module to <paramref name="dualGridRuleTile"/> and rebuilds its render tilemap.
    /// </summary>
    private static void UpdateDualGridTile(DualGridTilemapModule dualGridTilemapModule, DualGridRuleTile dualGridRuleTile)
    {
        dualGridTilemapModule.RenderTile = dualGridRuleTile;
        dualGridTilemapModule.RefreshRenderTilemap();
    }
}
