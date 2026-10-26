using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unsaved world generators for the world generation tests, built in code from the project's area generators, terrain
/// style, movement profile, ground materials and Weasel, which they only read: worlds of any size, the Overworld's
/// Phase 0 settings (Docs/WorldGeneration.md: Behaviour: The Overworld) with the wilds' prop settings of the project's
/// Overworld generator (<see cref="WithWildsProps"/>), and chunk grids built in memory as
/// <see cref="WorldAuthoring.CreateChunk"/> builds a chunk's. <see cref="Destroy"/> destroys everything it made.
/// </summary>
public sealed class TestWorldGenerators
{
    public const string Clearing = "clearing";
    public const string VillageForest = "villageForest";
    public const string Lakeside = "lakeside";
    public const string Snowfield = "snowfield";
    public const string Coast = "coast";
    public const string LavaFields = "lavaFields";
    public static readonly Vector2Int Origin = new(8192, 0);

    private const string GeneratorFolder = "Assets/Settings/Generation";
    private const string OverworldGeneratorPath = GeneratorFolder + "/WorldGenerator_Overworld.asset";
    private static readonly string[] WildsPropFields = { "groves", "edgeReach", "propPathClearance", "propEntryClearance", "wildsProps" };
    private const string MaterialFolder = "Assets/Settings/Terrain/Materials";
    private const string ProfilePath = "Assets/Settings/AI/TerrainMovement_Default.asset";
    private const string WeaselPath = "Assets/Prefabs/Mobs/Weasel.prefab";

    private readonly List<Object> created = new();

    /// <summary>
    /// Loads the project's style, profile, materials and mob.
    /// </summary>
    public TestWorldGenerators()
    {
        Style = AssetDatabase.LoadAssetAtPath<TerrainStyle>(WorldAuthoring.StylePath);
        Profile = AssetDatabase.LoadAssetAtPath<TerrainMovementProfile2D>(ProfilePath);
        Weasel = AssetDatabase.LoadAssetAtPath<GameObject>(WeaselPath).GetComponent<MobController>();
        Materials = new WorldGenerator.GroundMaterials(GroundMaterial("Grass"), GroundMaterial("Sand"), GroundMaterial("Ocean"), GroundMaterial("Stone"), GroundMaterial("Gravel"), GroundMaterial("Plaza"), GroundMaterial("Weeds"), GroundMaterial("Meadow"), GroundMaterial("TallGrass"));
    }

    public TerrainStyle Style { get; }
    public TerrainMovementProfile2D Profile { get; }
    public MobController Weasel { get; }
    public WorldGenerator.GroundMaterials Materials { get; }

    /// <summary>
    /// An unsaved world generator for an unsaved world <paramref name="name"/> of <paramref name="size"/> chunks at
    /// <see cref="Origin"/>, which has <paramref name="chunks"/> (all of them when null), with the project's style,
    /// profile, materials and mob.
    /// </summary>
    public WorldGenerator Generator(string name, Vector2Int size, List<Vector2Int> chunks)
    {
        if (chunks == null)
        {
            chunks = new List<Vector2Int>();
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    chunks.Add(new Vector2Int(x, y));
                }
            }
        }

        WorldDefinition world = WorldDefinition.Create(name, Origin, size, chunks);
        created.Add(world);
        created.Add(world.RootScene);
        foreach (WorldDefinition.Chunk chunk in world.Chunks)
        {
            created.Add(chunk.Scene);
        }

        return For(world);
    }

    /// <summary>
    /// An unsaved world generator for <paramref name="world"/>, with the project's style, profile, materials and mob.
    /// </summary>
    public WorldGenerator For(WorldDefinition world)
    {
        return Track(WorldGenerator.Create(world, Style, Profile, Materials, Weasel));
    }

    /// <summary>
    /// The Overworld's Phase 0 settings on an unsaved generator of an unsaved world <paramref name="name"/>: the six
    /// regions at their seed 1 layouts on 5 x 4 chunks, the Clearing first, the eight road links and the wilds' props.
    /// </summary>
    public WorldGenerator Overworld(string name = "Overworld")
    {
        return Overworld(Generator(name, new Vector2Int(5, 4), null));
    }

    /// <summary>
    /// Gives <paramref name="generator"/> the Overworld's Phase 0 regions and road links and the wilds' props, and
    /// returns it.
    /// </summary>
    public static WorldGenerator Overworld(WorldGenerator generator)
    {
        WithWildsProps(generator);
        generator.SetRegions(
            new RegionPlacement(Clearing, Area("Clearing"), new Vector2Int(86, 124)),
            new RegionPlacement(VillageForest, Area("VillageForest"), new Vector2Int(200, 126)),
            new RegionPlacement(Lakeside, Area("Lakeside"), new Vector2Int(91, 59)),
            new RegionPlacement(Snowfield, Area("Snowfield"), new Vector2Int(91, 175)),
            new RegionPlacement(Coast, Area("Coast"), new Vector2Int(200, 200)),
            new RegionPlacement(LavaFields, Area("LavaFields"), new Vector2Int(186, 59)));
        generator.SetLinks(
            Link(Clearing, PortSide.East, VillageForest, PortSide.West, false, RoadLink.Surface.Stone),
            Link(Lakeside, PortSide.East, LavaFields, PortSide.West, false, RoadLink.Surface.Stone),
            Link(Snowfield, PortSide.East, Coast, PortSide.West, false, RoadLink.Surface.Stone),
            Link(Clearing, PortSide.North, Snowfield, PortSide.East, true, RoadLink.Surface.Gravel),
            Link(Clearing, PortSide.South, Lakeside, PortSide.East, true, RoadLink.Surface.Gravel),
            Link(LavaFields, PortSide.East, Coast, PortSide.East, false, RoadLink.Surface.Stone),
            Link(Snowfield, PortSide.West, Clearing, PortSide.West, false, RoadLink.Surface.Stone),
            Link(Lakeside, PortSide.West, Clearing, PortSide.West, false, RoadLink.Surface.Stone));
        return generator;
    }

    /// <summary>
    /// Gives <paramref name="generator"/> the wilds' prop settings of the project's Overworld generator asset (its groves,
    /// edge reach, clearances and scatter layers), which the tests only read, and returns it.
    /// </summary>
    public static WorldGenerator WithWildsProps(WorldGenerator generator)
    {
        WorldGenerator asset = AssetDatabase.LoadAssetAtPath<WorldGenerator>(OverworldGeneratorPath);
        Assert.That(asset, Is.Not.Null, "the Overworld's generator asset");
        SerializedObject source = new(asset);
        SerializedObject target = new(generator);
        foreach (string field in WildsPropFields)
        {
            target.CopyFromSerializedProperty(source.FindProperty(field));
        }

        target.ApplyModifiedPropertiesWithoutUndo();
        return generator;
    }

    /// <summary>
    /// A chunk of <paramref name="world"/> at <paramref name="coords"/> built in memory as
    /// <see cref="WorldAuthoring.CreateChunk"/> builds one: an enabled grid at the world's origin with its
    /// <see cref="WorldChunk"/>, a terrain renderer in the project's style drawing one empty grass layer, its navigation
    /// grid and its <see cref="AreaTerrain"/> listing the grass layer.
    /// </summary>
    public Grid NewChunk(WorldDefinition world, Vector2Int coords)
    {
        GameObject gridObject = Track(new GameObject($"Chunk {coords.x},{coords.y}", typeof(Grid)));
        gridObject.transform.position = new Vector3(world.Origin.x, world.Origin.y, 0f);
        gridObject.SetActive(false);
        gridObject.AddComponent<WorldChunk>().Configure(world, coords, null);
        Grid grid = gridObject.GetComponent<Grid>();
        gridObject.AddComponent<TerrainBlendRenderer>().Configure(Style);
        GroundLayerMigration.AddLayer(grid, GroundMaterial("Grass"));
        WorldAuthoring.SetUpNavigation(grid);
        WorldAuthoring.SetUpTerrainEdits(grid);
        gridObject.SetActive(true);
        return grid;
    }

    /// <summary>
    /// The project's area generator <paramref name="name"/> (Generator_name), which the tests only read.
    /// </summary>
    public static AreaGenerator Area(string name)
    {
        AreaGenerator generator = AssetDatabase.LoadAssetAtPath<AreaGenerator>($"{GeneratorFolder}/Generator_{name}.asset");
        Assert.That(generator, Is.Not.Null, $"the project's {name} generator");
        return generator;
    }

    /// <summary>
    /// A road link 3 cells wide for stone and 2 for gravel.
    /// </summary>
    public static RoadLink Link(string from, PortSide fromSide, string to, PortSide toSide, bool toRoad, RoadLink.Surface surface)
    {
        return new RoadLink(from, fromSide, to, toSide, toRoad, surface, surface == RoadLink.Surface.Gravel ? 2 : 3);
    }

    /// <summary>
    /// The project's ground material <paramref name="name"/>.
    /// </summary>
    public static TerrainMaterial GroundMaterial(string name)
    {
        TerrainMaterial material = AssetDatabase.LoadAssetAtPath<TerrainMaterial>($"{MaterialFolder}/{name}.asset");
        Assert.That(material, Is.Not.Null, $"the project's {name} material");
        return material;
    }

    /// <summary>
    /// Records <paramref name="target"/> for <see cref="Destroy"/> and returns it.
    /// </summary>
    public T Track<T>(T target) where T : Object
    {
        created.Add(target);
        return target;
    }

    /// <summary>
    /// Clears the undo records of the objects it made and destroys them, the last made first.
    /// </summary>
    public void Destroy()
    {
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] == null)
            {
                continue;
            }

            if (created[i] is GameObject made)
            {
                foreach (Transform part in made.GetComponentsInChildren<Transform>(true))
                {
                    foreach (Component component in part.GetComponents<Component>())
                    {
                        Undo.ClearUndo(component);
                    }

                    Undo.ClearUndo(part.gameObject);
                }
            }

            Object.DestroyImmediate(created[i]);
        }

        created.Clear();
    }
}
