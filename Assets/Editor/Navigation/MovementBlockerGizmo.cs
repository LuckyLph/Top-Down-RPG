using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws what keeps players off the cells of every enabled <see cref="NavigationGrid2D"/>, selected or not, in edit and
/// play mode: a translucent fill on each blocked cell inside the grid's walkable bounds (a chunk's cells, on a chunk's
/// grid), coloured by what blocks it (the cells raised ground's cliff face covers whole in yellow), a white line along
/// every side where a walkable cell meets a blocked one, and a yellow line along the cliff of raised ground, which blocks
/// steps across it rather than cells. Across the edge of the bounds it reads the cell beyond from the grid's navigation:
/// outside an area, or in a chunk not loaded, nothing is walkable, while a loaded neighbouring chunk's cells are read
/// from its page, so no line is drawn along a border the ground crosses. Walkability is the players' movement
/// profile, read from the project's <see cref="PlayerControlSettings"/>. Toggled from the Tools menu.
/// </summary>
public static class MovementBlockerGizmo
{
    private const string EnabledPrefKey = "TopDownRPG.Gizmos.MovementBlockers";
    private const string ToggleMenuPath = ProjectScenes.MenuRoot + "Gizmos/Movement Blockers";

    private static readonly Color EdgeColor = new(1f, 1f, 1f, 0.75f);
    private static readonly Color CliffColor = new(1f, 0.9f, 0.2f, 0.95f);
    private static readonly Color NoGroundColor = new(0.5f, 0.5f, 0.5f, 0.3f);
    private static readonly Color TerrainColor = new(0.95f, 0.3f, 0.9f, 0.3f);
    private static readonly Color WallColor = new(1f, 0.25f, 0.2f, 0.35f);
    private static readonly Color ObstacleColor = new(1f, 0.55f, 0.1f, 0.35f);
    private static readonly Color CliffFaceColor = new(1f, 0.9f, 0.2f, 0.3f);

    private static readonly Dictionary<NavigationGrid2D, BlockerMap> mapsByGrid = new();
    private static PlayerControlSettings playerSettings;

    public static bool Enabled
    {
        get => EditorPrefs.GetBool(EnabledPrefKey, true);
        set
        {
            EditorPrefs.SetBool(EnabledPrefKey, value);
            SceneView.RepaintAll();
        }
    }

    /// <summary>
    /// A row of neighbouring blocked cells with the same blocker: its first cell, how many cells it spans along x, and
    /// its world-space centre and size.
    /// </summary>
    internal readonly struct BlockedRun
    {
        /// <summary>
        /// A run of <paramref name="length"/> cells from <paramref name="start"/>, blocked by <paramref name="blocker"/>.
        /// </summary>
        public BlockedRun(Vector3Int start, int length, NavigationGrid2D.Blocker blocker, Vector3 center, Vector3 size)
        {
            Start = start;
            Length = length;
            Blocker = blocker;
            Center = center;
            Size = size;
        }

        public Vector3Int Start { get; }
        public int Length { get; }
        public NavigationGrid2D.Blocker Blocker { get; }
        public Vector3 Center { get; }
        public Vector3 Size { get; }
    }

    /// <summary>
    /// Line segments as pairs of world-space points in a buffer that grows as needed and is reused when cleared.
    /// </summary>
    internal sealed class LineList
    {
        private Vector3[] points = Array.Empty<Vector3>();

        public int PointCount { get; private set; }
        public ReadOnlySpan<Vector3> Points => points.AsSpan(0, PointCount);

        /// <summary>
        /// Empties the list, keeping its buffer.
        /// </summary>
        public void Clear()
        {
            PointCount = 0;
        }

        /// <summary>
        /// Appends a line from <paramref name="from"/> to <paramref name="to"/>, growing the buffer when it is full.
        /// </summary>
        public void Add(Vector3 from, Vector3 to)
        {
            if (PointCount + 2 > points.Length)
            {
                Array.Resize(ref points, Mathf.Max(64, points.Length * 2));
            }

            points[PointCount++] = from;
            points[PointCount++] = to;
        }
    }

    /// <summary>
    /// One grid's blocked cells under one movement profile: the runs to fill, the edges to outline and the cliff's
    /// lines, rebuilt only when the grid's <see cref="NavigationGrid2D.Version"/>, the profile or the profile's version
    /// changes.
    /// </summary>
    internal sealed class BlockerMap
    {
        private readonly List<BlockedRun> runs = new();
        private NavigationGrid2D.Blocker[] blockers = Array.Empty<NavigationGrid2D.Blocker>();
        private int gridVersion = -1;
        private TerrainMovementProfile2D profile;
        private int profileVersion;

        public IReadOnlyList<BlockedRun> Runs => runs;
        public LineList Edges { get; } = new();
        public LineList Cliffs { get; } = new();

        /// <summary>
        /// Rebuilds the runs and lines of <paramref name="grid"/> under <paramref name="movementProfile"/> (null = every
        /// terrain walkable) unless they are already current.
        /// </summary>
        public void Refresh(NavigationGrid2D grid, TerrainMovementProfile2D movementProfile)
        {
            int version = movementProfile != null ? movementProfile.Version : 0;
            if (grid.Version == gridVersion && movementProfile == profile && version == profileVersion)
            {
                return;
            }

            gridVersion = grid.Version;
            profile = movementProfile;
            profileVersion = version;
            Build(grid, movementProfile);
        }

        /// <summary>
        /// Classifies every cell of the grid's walkable bounds, merges each row's neighbouring blocked cells with the same
        /// blocker into runs, records the sides of every walkable cell that face a blocked cell or the bounds, and the
        /// cliff's sides.
        /// </summary>
        private void Build(NavigationGrid2D grid, TerrainMovementProfile2D movementProfile)
        {
            runs.Clear();
            Edges.Clear();
            Cliffs.Clear();
            int count = grid.CellCount;
            if (count == 0)
            {
                return;
            }

            if (blockers.Length < count)
            {
                blockers = new NavigationGrid2D.Blocker[count];
            }

            for (int i = 0; i < count; i++)
            {
                blockers[i] = grid.GetBlocker(grid.GetCell(i), movementProfile);
            }

            BoundsInt bounds = grid.WalkableBounds;
            int width = bounds.size.x;
            int height = bounds.size.y;
            Vector3 origin = grid.CellToWorldCenter(bounds.min);
            Vector3 right = grid.CellToWorldCenter(bounds.min + Vector3Int.right) - origin;
            Vector3 up = grid.CellToWorldCenter(bounds.min + Vector3Int.up) - origin;
            AddRuns(grid, width, count / width, right, up);
            AddLines(grid, movementProfile, width, height, count, right * 0.5f, up * 0.5f);
        }

        /// <summary>
        /// Adds a run for each stretch of neighbouring cells along a row that share a blocker other than
        /// <see cref="NavigationGrid2D.Blocker.None"/>.
        /// </summary>
        private void AddRuns(NavigationGrid2D grid, int width, int rows, Vector3 right, Vector3 up)
        {
            for (int row = 0; row < rows; row++)
            {
                int rowStart = row * width;
                int x = 0;
                while (x < width)
                {
                    NavigationGrid2D.Blocker blocker = blockers[rowStart + x];
                    int runStart = x;
                    while (x < width && blockers[rowStart + x] == blocker)
                    {
                        x++;
                    }

                    if (blocker == NavigationGrid2D.Blocker.None)
                    {
                        continue;
                    }

                    int length = x - runStart;
                    Vector3Int start = grid.GetCell(rowStart + runStart);
                    Vector3 center = grid.CellToWorldCenter(start) + right * ((length - 1) * 0.5f);
                    Vector3 size = new(right.magnitude * length, up.magnitude, 0f);
                    runs.Add(new BlockedRun(start, length, blocker, center, size));
                }
            }
        }

        /// <summary>
        /// Adds an edge along each side of a walkable cell that faces a blocked cell, inside the bounds or beyond them,
        /// and a cliff line along each cliff side, once per side: every cell's right and top sides, and the left and
        /// bottom sides of the cells along the bounds.
        /// </summary>
        private void AddLines(NavigationGrid2D grid, TerrainMovementProfile2D movementProfile, int width, int height, int count, Vector3 halfRight, Vector3 halfUp)
        {
            for (int i = 0; i < count; i++)
            {
                int x = i % width;
                int y = i / width % height;
                Vector3Int cell = grid.GetCell(i);
                Vector3 center = grid.CellToWorldCenter(cell);
                Vector3 bottomLeft = center - halfRight - halfUp;
                Vector3 bottomRight = center + halfRight - halfUp;
                Vector3 topLeft = center - halfRight + halfUp;
                Vector3 topRight = center + halfRight + halfUp;
                byte cliffSides = grid.GetCliffSides(cell);
                AddCliff(cliffSides, NavigationGrid2D.RightSide, bottomRight, topRight);
                AddCliff(cliffSides, NavigationGrid2D.UpSide, topLeft, topRight);
                AddCliff(x == 0 ? cliffSides : (byte)0, NavigationGrid2D.LeftSide, bottomLeft, topLeft);
                AddCliff(y == 0 ? cliffSides : (byte)0, NavigationGrid2D.DownSide, bottomLeft, bottomRight);
                if (blockers[i] != NavigationGrid2D.Blocker.None)
                {
                    continue;
                }

                if (x == 0 ? IsBlockedBeyond(grid, cell + Vector3Int.left, movementProfile) : blockers[i - 1] != NavigationGrid2D.Blocker.None)
                {
                    Edges.Add(bottomLeft, topLeft);
                }

                if (x == width - 1 ? IsBlockedBeyond(grid, cell + Vector3Int.right, movementProfile) : blockers[i + 1] != NavigationGrid2D.Blocker.None)
                {
                    Edges.Add(bottomRight, topRight);
                }

                if (y == 0 ? IsBlockedBeyond(grid, cell + Vector3Int.down, movementProfile) : blockers[i - width] != NavigationGrid2D.Blocker.None)
                {
                    Edges.Add(bottomLeft, bottomRight);
                }

                if (y == height - 1 ? IsBlockedBeyond(grid, cell + Vector3Int.up, movementProfile) : blockers[i + width] != NavigationGrid2D.Blocker.None)
                {
                    Edges.Add(topLeft, topRight);
                }
            }
        }

        /// <summary>
        /// Whether <paramref name="cell"/>, beyond the grid's bounds, keeps the profile off it in the grid's navigation.
        /// </summary>
        private static bool IsBlockedBeyond(NavigationGrid2D grid, Vector3Int cell, TerrainMovementProfile2D movementProfile)
        {
            return grid.GetBlocker(cell, movementProfile) != NavigationGrid2D.Blocker.None;
        }

        /// <summary>
        /// Adds a cliff line from <paramref name="from"/> to <paramref name="to"/> when <paramref name="cliffSides"/> has
        /// <paramref name="side"/>.
        /// </summary>
        private void AddCliff(byte cliffSides, byte side, Vector3 from, Vector3 to)
        {
            if ((cliffSides & side) != 0)
            {
                Cliffs.Add(from, to);
            }
        }
    }

    /// <summary>
    /// Flips the gizmo on or off from the Tools menu.
    /// </summary>
    [MenuItem(ToggleMenuPath)]
    private static void Toggle()
    {
        Enabled = !Enabled;
    }

    /// <summary>
    /// Keeps the menu checkmark in step with the setting; the entry is always available.
    /// </summary>
    [MenuItem(ToggleMenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(ToggleMenuPath, Enabled);
        return true;
    }

    /// <summary>
    /// Fills the blocked runs of <paramref name="grid"/> by blocker and outlines where walking stops, building the grid
    /// first outside Play Mode when it is stale.
    /// </summary>
    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawMovementBlockers(NavigationGrid2D grid, GizmoType gizmoType)
    {
        if (!Enabled || !grid.isActiveAndEnabled)
        {
            return;
        }

        grid.BuildGizmoGridIfStale();
        if (!grid.IsBuilt)
        {
            return;
        }

        BlockerMap map = MapFor(grid);
        map.Refresh(grid, PlayerProfile());
        for (int i = 0; i < map.Runs.Count; i++)
        {
            BlockedRun run = map.Runs[i];
            Gizmos.color = ColorOf(run.Blocker);
            Gizmos.DrawCube(run.Center, run.Size);
        }

        DrawLines(map.Edges, EdgeColor);
        DrawLines(map.Cliffs, CliffColor);
    }

    /// <summary>
    /// Draws <paramref name="lines"/> in <paramref name="color"/> in one call; nothing when empty.
    /// </summary>
    private static void DrawLines(LineList lines, Color color)
    {
        if (lines.PointCount == 0)
        {
            return;
        }

        Gizmos.color = color;
        Gizmos.DrawLineList(lines.Points);
    }

    /// <summary>
    /// The cached map of <paramref name="grid"/>, created on first use; dropping the maps of destroyed grids then.
    /// </summary>
    private static BlockerMap MapFor(NavigationGrid2D grid)
    {
        if (mapsByGrid.TryGetValue(grid, out BlockerMap map))
        {
            return map;
        }

        List<NavigationGrid2D> destroyed = new();
        foreach (NavigationGrid2D cached in mapsByGrid.Keys)
        {
            if (cached == null)
            {
                destroyed.Add(cached);
            }
        }

        foreach (NavigationGrid2D cached in destroyed)
        {
            mapsByGrid.Remove(cached);
        }

        map = new BlockerMap();
        mapsByGrid.Add(grid, map);
        return map;
    }

    /// <summary>
    /// The players' movement profile, from the first <see cref="PlayerControlSettings"/> asset in the project, or null
    /// (every terrain walkable) when there is none.
    /// </summary>
    private static TerrainMovementProfile2D PlayerProfile()
    {
        if (playerSettings == null)
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(PlayerControlSettings));
            playerSettings = guids.Length > 0
                ? AssetDatabase.LoadAssetAtPath<PlayerControlSettings>(AssetDatabase.GUIDToAssetPath(guids[0]))
                : null;
        }

        return playerSettings != null ? playerSettings.MovementProfile : null;
    }

    /// <summary>
    /// The fill colour of cells kept off by <paramref name="blocker"/>.
    /// </summary>
    private static Color ColorOf(NavigationGrid2D.Blocker blocker)
    {
        return blocker switch
        {
            NavigationGrid2D.Blocker.Terrain => TerrainColor,
            NavigationGrid2D.Blocker.Wall => WallColor,
            NavigationGrid2D.Blocker.Obstacle => ObstacleColor,
            NavigationGrid2D.Blocker.CliffFace => CliffFaceColor,
            _ => NoGroundColor,
        };
    }
}
