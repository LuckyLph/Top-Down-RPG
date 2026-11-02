using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for the walls' light shadows (Docs/Lighting.md, Phase 7): the walls prefab carries a Shadow Caster 2D shaped by
/// its collider, casting on every sorting layer without URP's trim; the walls <see cref="WorldAuthoring.AddWalls"/> gives
/// a grid keep it, shaped by their own collider, and <see cref="WorldAuthoring.AddWallShadows"/> gives walls without one
/// the same caster; every chunk of the project's worlds with walls has it; and saving a scene leaves the casters' shadow
/// meshes out of it (<see cref="ShadowCasterSceneSaving"/>).
/// </summary>
public class WallShadowTests
{
    private const string WallsName = WorldAuthoring.WallsName;
    private static readonly string[] WorldFolders = { WorldAuthoring.WorldsFolder, "Assets/Dev/Worlds" };

    private readonly List<Object> created = new();

    /// <summary>
    /// Destroys every object the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object made in created)
        {
            if (made != null)
            {
                Object.DestroyImmediate(made);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// The walls prefab's data tilemap carries a Shadow Caster 2D, on, shaped through URP's collider shape by the walls'
    /// own tilemap collider, casting a light's shadow on every sorting layer, leaving the walls' own cells lit, with no
    /// trim.
    /// </summary>
    [Test]
    public void TheWallsPrefab_CastsALightsShadowFromItsCollider_OnEverySortingLayer()
    {
        GameObject walls = AssetDatabase.LoadAssetAtPath<GameObject>(WorldAuthoring.WallsPrefabPath);
        AssertCastsFromItsCollider(walls);
    }

    /// <summary>
    /// The walls a grid is given are shaped by their own collider, not the prefab's; walls without a caster are given
    /// one by <see cref="WorldAuthoring.AddWallShadows"/>, which returns the one walls already have.
    /// </summary>
    [Test]
    public void AddedWalls_CastFromTheirOwnCollider_AndWallsWithoutACasterAreGivenOne()
    {
        GameObject gridObject = Track(new GameObject("WallShadowGrid", typeof(Grid)));
        DualGridTilemapModule module = WorldAuthoring.AddWalls(gridObject.GetComponent<Grid>());
        AssertCastsFromItsCollider(module.gameObject);
        ShadowCaster2D caster = module.GetComponent<ShadowCaster2D>();
        Assert.That(WorldAuthoring.AddWallShadows(module.gameObject), Is.SameAs(caster), "Walls with a caster keep it.");

        Object.DestroyImmediate(caster);
        Assert.That(WorldAuthoring.AddWallShadows(module.gameObject), Is.Not.Null);
        AssertCastsFromItsCollider(module.gameObject);
    }

    /// <summary>
    /// Every chunk scene of the project's worlds with walls has their caster, shaped by their collider.
    /// </summary>
    [Test]
    public void EveryWorldsWalls_CastALightsShadow()
    {
        int checkedWalls = 0;
        foreach (string folder in WorldFolders)
        {
            foreach (string path in Directory.GetFiles(folder, "*.unity", SearchOption.AllDirectories))
            {
                string scenePath = path.Replace('\\', '/');
                if (!scenePath.Contains("/Chunks/") || !File.ReadAllText(scenePath).Contains($"m_Name: {WallsName}"))
                {
                    continue;
                }

                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                try
                {
                    foreach (TilemapCollider2D collider in SceneQuery.FindAll<TilemapCollider2D>(scene))
                    {
                        if (collider.gameObject.name == WallsName)
                        {
                            AssertCastsFromItsCollider(collider.gameObject);
                            checkedWalls++;
                        }
                    }
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        Assert.That(checkedWalls, Is.GreaterThanOrEqualTo(11), "The Sandbox's, its interior's, Testlands' and the stress world's walls.");
    }

    /// <summary>
    /// Saving a scene marks its casters' shadow meshes, made in the editor, not to be saved, and leaves an asset mesh
    /// alone.
    /// </summary>
    [Test]
    public void SavingAScene_LeavesTheCastersShadowMeshesOut()
    {
        Scene scene = SceneManager.GetActiveScene();
        ShadowCaster2D caster = Track(new GameObject("Wall")).AddComponent<ShadowCaster2D>();
        caster.Update();
        Assert.That(caster.mesh, Is.Not.Null, "The editor makes the caster a mesh.");
        Assert.That(caster.mesh.hideFlags & HideFlags.DontSaveInEditor, Is.EqualTo(HideFlags.None));

        Assert.That(ShadowCasterSceneSaving.KeepMeshesOut(scene), Is.EqualTo(1));
        Assert.That(caster.mesh.hideFlags & HideFlags.DontSaveInEditor, Is.EqualTo(HideFlags.DontSaveInEditor));
        Assert.That(ShadowCasterSceneSaving.KeepMeshesOut(scene), Is.Zero, "Each mesh is marked once.");
        Object.DestroyImmediate(caster.mesh);
    }

    /// <summary>
    /// Asserts <paramref name="walls"/> carry an enabled Shadow Caster 2D shaped by their own tilemap collider, on every
    /// sorting layer, casting without self shadows, untrimmed.
    /// </summary>
    private static void AssertCastsFromItsCollider(GameObject walls)
    {
        ShadowCaster2D caster = walls.GetComponent<ShadowCaster2D>();
        Assert.That(caster, Is.Not.Null, $"{walls.name} in {walls.scene.name} stops lights.");
        Assert.That(caster.enabled, Is.True, walls.name);
        Assert.That(caster.castingOption, Is.EqualTo(ShadowCaster2D.ShadowCastingOptions.CastShadow), walls.name);
        SerializedObject serialized = new(caster);
        Assert.That(serialized.FindProperty("m_ShadowCastingSource").intValue, Is.EqualTo(2), "Shaped by a shape provider.");
        Assert.That(serialized.FindProperty("m_ShadowShape2DComponent").objectReferenceValue, Is.EqualTo(walls.GetComponent<TilemapCollider2D>()), "Shaped by the walls' own collider.");
        Assert.That(serialized.FindProperty("m_ShadowMesh.m_TrimEdge").floatValue, Is.Zero, "Untrimmed.");
        SerializedProperty layers = serialized.FindProperty("m_ApplyToSortingLayers");
        List<int> ids = new();
        for (int i = 0; i < layers.arraySize; i++)
        {
            ids.Add(layers.GetArrayElementAtIndex(i).intValue);
        }

        foreach (SortingLayer layer in SortingLayer.layers)
        {
            Assert.That(ids, Does.Contain(layer.id), $"{walls.name} shadows the {layer.name} sorting layer.");
        }
    }

    /// <summary>
    /// Adds <paramref name="made"/> to the objects destroyed in <see cref="TearDown"/>.
    /// </summary>
    private T Track<T>(T made) where T : Object
    {
        created.Add(made);
        return made;
    }
}
