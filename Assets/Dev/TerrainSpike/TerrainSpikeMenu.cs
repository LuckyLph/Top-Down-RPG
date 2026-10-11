#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Editor menu for the terrain blending spike (Tools/TopDownRPG/Dev/Terrain Spike): attaches a never-saved
/// <see cref="TerrainSpikeRenderer"/> to the active area's ground layers, detaches it, shows or hides it to compare
/// with the render tilemaps, and switches its look.
/// </summary>
public static class TerrainSpikeMenu
{
    private const string Root = "Tools/TopDownRPG/Dev/Terrain Spike/";
    private const string Folder = "Assets/Dev/TerrainSpike/";
    private const string ObjectName = "TerrainSpike (not saved)";
    private static readonly string[] LayerNames = { "GrassTilemap", "GravelTilemap", "StoneTilemap" };
    private static readonly string[] MaterialNames = { "grass", "gravel", "stone" };

    /// <summary>
    /// Replaces any spike with a new one over the active scene's grid, on an object that is never saved.
    /// </summary>
    [MenuItem(Root + "Attach To Active Area")]
    public static TerrainSpikeRenderer Attach()
    {
        Detach();
        Scene scene = SceneManager.GetActiveScene();
        Grid grid = FindGrid(scene);
        if (grid == null)
        {
            Debug.LogError($"Terrain spike: no Grid with {string.Join(", ", LayerNames)} in {scene.name}.");
            return null;
        }
        var layers = new Tilemap[LayerNames.Length];
        for (int i = 0; i < LayerNames.Length; i++)
        {
            Transform child = grid.transform.Find(LayerNames[i]);
            layers[i] = child != null ? child.GetComponent<Tilemap>() : null;
        }

        var spikeObject = new GameObject(ObjectName) { hideFlags = HideFlags.DontSave };
        SceneManager.MoveGameObjectToScene(spikeObject, scene);
        spikeObject.SetActive(false);
        var spike = spikeObject.AddComponent<TerrainSpikeRenderer>();
        spike.Configure(grid, layers, AssetDatabase.LoadAssetAtPath<Shader>(Folder + "TerrainSpike.shadergraph"),
            LoadTextures("albedo"), LoadTextures("field"), LoadTextures("height"));
        spikeObject.SetActive(true);
        return spike;
    }

    /// <summary>
    /// Destroys every spike, including one left behind by a closed scene.
    /// </summary>
    [MenuItem(Root + "Detach")]
    public static void Detach()
    {
        foreach (TerrainSpikeRenderer spike in FindSpikes())
        {
            Object.DestroyImmediate(spike.gameObject);
        }
    }

    /// <summary>
    /// Shows or hides the spike, so the render tilemaps underneath show today's tiles.
    /// </summary>
    [MenuItem(Root + "Toggle Visible")]
    public static void ToggleVisible()
    {
        foreach (TerrainSpikeRenderer spike in FindSpikes())
        {
            spike.gameObject.SetActive(!spike.gameObject.activeSelf);
        }
        SceneView.RepaintAll();
    }

    /// <summary>
    /// Switches every spike between the crisp and soft looks.
    /// </summary>
    [MenuItem(Root + "Flip Look")]
    public static void FlipLook()
    {
        foreach (TerrainSpikeRenderer spike in FindSpikes())
        {
            spike.FlipLook();
            Debug.Log($"Terrain spike look: {spike.CurrentLook}", spike);
        }
        SceneView.RepaintAll();
    }

    /// <summary>
    /// Every spike object, active or not, including one left behind by a closed scene (FindObjectsByType skips
    /// objects flagged DontSave).
    /// </summary>
    private static List<TerrainSpikeRenderer> FindSpikes()
    {
        var spikes = new List<TerrainSpikeRenderer>();
        foreach (TerrainSpikeRenderer spike in Resources.FindObjectsOfTypeAll<TerrainSpikeRenderer>())
        {
            if (!EditorUtility.IsPersistent(spike))
            {
                spikes.Add(spike);
            }
        }
        return spikes;
    }

    /// <summary>
    /// Detaches the spike when its scene closes or play mode starts or ends, since an object flagged DontSave outlives
    /// all three.
    /// </summary>
    [InitializeOnLoadMethod]
    private static void DetachWithScene()
    {
        EditorSceneManager.sceneClosing += (scene, removing) =>
        {
            foreach (TerrainSpikeRenderer spike in FindSpikes())
            {
                if (spike.gameObject.scene == scene)
                {
                    Object.DestroyImmediate(spike.gameObject);
                }
            }
        };
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
            {
                Detach();
            }
        };
    }

    /// <summary>
    /// The scene's grid that holds a grass layer.
    /// </summary>
    private static Grid FindGrid(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Grid grid in root.GetComponentsInChildren<Grid>(true))
            {
                if (grid.transform.Find(LayerNames[0]) != null)
                {
                    return grid;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// The spike's grass, gravel and stone textures of one kind (albedo, field or height); missing ones are null.
    /// </summary>
    private static Texture2D[] LoadTextures(string kind)
    {
        var textures = new Texture2D[MaterialNames.Length];
        for (int i = 0; i < MaterialNames.Length; i++)
        {
            textures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}Textures/{MaterialNames[i]}_{kind}.png");
        }
        return textures;
    }
}
#endif
