using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Tilemaps;
using VContainer;

/// <summary>
/// The tilemaps of an area that gameplay may change at runtime (a plain ground layer filled with its tile, or the
/// dual-grid walls painted through their module), and the way to change them so every machine agrees.
/// Only the machine with authority (the host, or offline) edits, through <see cref="TrySetCells"/>: the tiles change
/// and every changed cell is recorded in <see cref="Edits"/>, the last write per cell winning.
/// <see cref="NetworkAreaTerrain"/> sends that record to clients, which apply it with <see cref="ApplyReplicated"/>,
/// late joiners included. Navigation follows the tile changes by itself. Edits last while the area scene is loaded;
/// they are not saved.
/// </summary>
[DisallowMultipleComponent]
public class AreaTerrain : MonoBehaviour
{
    /// <summary>One editable tilemap, and the tile written into its filled cells.</summary>
    [Serializable]
    public class Layer
    {
        [SerializeField] private Tilemap tilemap;
        [Tooltip("Tile written into filled cells: a ground layer's plain tile (Tile_Grass and so on). Leave empty on a dual-grid data tilemap (the walls): its module paints its own data tile.")]
        [SerializeField] private TileBase fillTile;

        /// <summary>
        /// Creates an empty layer, for Unity serialization and the inspector list.
        /// </summary>
        public Layer()
        {
        }

        /// <summary>
        /// Creates a layer for <paramref name="tilemap"/> that fills cells with <paramref name="fillTile"/> (null for a
        /// dual-grid data tilemap).
        /// </summary>
        public Layer(Tilemap tilemap, TileBase fillTile)
        {
            this.tilemap = tilemap;
            this.fillTile = fillTile;
        }

        public Tilemap Tilemap => tilemap;
        public TileBase FillTile => fillTile;
    }

    private const int MaxLayers = byte.MaxValue + 1;

    [Tooltip("Tilemaps that may change at runtime. The index in this list identifies a layer on every machine.")]
    [SerializeField] private List<Layer> layers = new();
    [Tooltip("Network prefab with a NetworkAreaTerrain that the host spawns to replicate this area's edits.")]
    [SerializeField] private NetworkObject syncPrefab;

    private readonly List<TerrainEdit> edits = new();
    private readonly Dictionary<(int, Vector3Int), int> editIndexByCell = new();
    private readonly List<Vector3Int> filledCells = new();
    private readonly List<Vector3Int> emptiedCells = new();
    private IGameAuthority authority;

    /// <summary>Raised on the authority with the index in <see cref="Edits"/> of each cell edit added or replaced.</summary>
    public event Action<int> EditRecorded;

    public IReadOnlyList<TerrainEdit> Edits => edits;
    public NetworkObject SyncPrefab => syncPrefab;

    /// <summary>
    /// Logs an error for too many layers, for a layer without a tilemap, and for a plain tilemap layer without a fill
    /// tile.
    /// </summary>
    private void Awake()
    {
        if (layers.Count > MaxLayers)
        {
            Debug.LogError($"{name} has {layers.Count} terrain layers; only the first {MaxLayers} can be edited.", this);
        }

        for (int i = 0; i < layers.Count; i++)
        {
            Layer layer = layers[i];
            if (layer == null || layer.Tilemap == null)
            {
                Debug.LogError($"{name}: terrain layer {i} has no tilemap.", this);
            }
            else if (layer.FillTile == null && !layer.Tilemap.TryGetComponent(out DualGridTilemapModule _))
            {
                Debug.LogError($"{name}: terrain layer {i} ({layer.Tilemap.name}) needs a fill tile, since it is not a dual-grid data tilemap.", this);
            }
        }
    }

    /// <summary>
    /// Receives the authority check that gates <see cref="TrySetCells"/>.
    /// </summary>
    [Inject]
    public void Construct(IGameAuthority gameAuthority)
    {
        authority = gameAuthority;
    }

    /// <summary>
    /// Sets the layers in code, for tests.
    /// </summary>
    public void Configure(params Layer[] terrainLayers)
    {
        layers.Clear();
        layers.AddRange(terrainLayers);
    }

    /// <summary>
    /// Fills or empties <paramref name="cells"/> on <paramref name="layer"/> and records them for clients. Returns
    /// false, changing nothing, on a machine without authority or for a tilemap that is not one of the layers.
    /// </summary>
    public bool TrySetCells(Tilemap layer, IReadOnlyList<Vector3Int> cells, bool filled)
    {
        if (authority == null || !authority.IsAuthoritative)
        {
            return false;
        }

        int layerIndex = IndexOf(layer);
        if (layerIndex < 0)
        {
            Debug.LogError($"{(layer != null ? layer.name : "null")} is not one of {name}'s editable terrain layers.", this);
            return false;
        }

        Write(layerIndex, cells, filled);
        for (int i = 0; i < cells.Count; i++)
        {
            Record(new TerrainEdit(layerIndex, cells[i], filled));
        }

        return true;
    }

    /// <summary>
    /// Applies edits the host made, without recording them. Edits for a layer this area does not have are skipped.
    /// </summary>
    public void ApplyReplicated(IReadOnlyList<TerrainEdit> replicated)
    {
        for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            filledCells.Clear();
            emptiedCells.Clear();
            for (int i = 0; i < replicated.Count; i++)
            {
                TerrainEdit edit = replicated[i];
                if (edit.Layer == layerIndex)
                {
                    (edit.Filled ? filledCells : emptiedCells).Add(edit.Cell);
                }
            }

            Write(layerIndex, filledCells, true);
            Write(layerIndex, emptiedCells, false);
        }
    }

    /// <summary>
    /// The layer index of <paramref name="tilemap"/> among the editable layers, or -1 when it is null or not one of
    /// them.
    /// </summary>
    private int IndexOf(Tilemap tilemap)
    {
        if (tilemap == null)
        {
            return -1;
        }

        for (int i = 0; i < layers.Count && i < MaxLayers; i++)
        {
            if (layers[i] != null && layers[i].Tilemap == tilemap)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Fills or empties <paramref name="cells"/> on the layer at <paramref name="layerIndex"/>: through its
    /// <see cref="DualGridTilemapModule"/> when it is a dual-grid data tilemap, else in one batch with the fill tile.
    /// </summary>
    private void Write(int layerIndex, IReadOnlyList<Vector3Int> cells, bool filled)
    {
        Layer layer = layers[layerIndex];
        if (cells.Count == 0 || layer == null || layer.Tilemap == null)
        {
            return;
        }

        if (layer.Tilemap.TryGetComponent(out DualGridTilemapModule module))
        {
            if (filled)
            {
                module.SetDataTiles(cells);
            }
            else
            {
                module.ClearDataTiles(cells);
            }

            return;
        }

        Vector3Int[] positions = new Vector3Int[cells.Count];
        TileBase[] tiles = new TileBase[cells.Count];
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i] = cells[i];
            tiles[i] = filled ? layer.FillTile : null;
        }

        layer.Tilemap.SetTiles(positions, tiles);
    }

    /// <summary>
    /// Adds <paramref name="edit"/> to <see cref="Edits"/>, replacing the earlier edit of the same layer and cell in
    /// place, and raises <see cref="EditRecorded"/> with its index.
    /// </summary>
    private void Record(TerrainEdit edit)
    {
        (int, Vector3Int) key = (edit.Layer, edit.Cell);
        if (editIndexByCell.TryGetValue(key, out int index))
        {
            edits[index] = edit;
        }
        else
        {
            index = edits.Count;
            edits.Add(edit);
            editIndexByCell[key] = index;
        }

        EditRecorded?.Invoke(index);
    }
}
