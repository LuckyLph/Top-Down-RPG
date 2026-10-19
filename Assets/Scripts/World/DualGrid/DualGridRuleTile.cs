using System;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;
using static DualGridRuleTile;

/// <summary>
/// The custom <see cref="RuleTile"/> used by the <see cref="DualGridTilemapModule"/> to generate tiles in the Render Tilemap.
/// </summary>
/// <remarks>
/// Avoid using this tile in a palette, as any other data tile can be used.
/// <para></para>
/// This tile type will be used in all Render Tilemaps.
/// </remarks>
[Serializable]
[CreateAssetMenu(fileName = "DualGridRuleTile", menuName = "Scriptable Objects/DualGridRuleTile")]
public class DualGridRuleTile : RuleTile<DualGridNeighbor>
{
    [SerializeField, HideInInspector, FormerlySerializedAs("_originalTexture")] private Texture2D originalTexture;
    public Texture2D OriginalTexture { get => originalTexture; internal set => originalTexture = value; }

    [SerializeField, HideInInspector, FormerlySerializedAs("_dataTile")] private DualGridDataTile dataTile;
    /// <summary>
    /// The Data Tile is a tile generated from this Dual Grid Rule Tile to populate the DataTilemap.
    /// </summary>
    public DualGridDataTile DataTile
    {
        get
        {
            if (dataTile == null)
            {
                Debug.LogError($"The DualGridRuleTile {name} does not have an associated Data Tile. If this Asset was created in a previous version of DualGrid, update or re-create the Asset to potentially fix this error.", this);
                throw new ArgumentNullException($"The DualGridRuleTile {name} does not have an associated Data Tile.");
            }

            return dataTile;
        }
    }

    private DualGridTilemapModule dualGridTilemapModule;

    private Tilemap dataTilemap;

    private Tilemap lastOriginTilemap;

    /// <summary>
    /// The neighbor conditions a dual-grid tiling rule can require of each of the 4 data tiles around a render tile.
    /// </summary>
    public class DualGridNeighbor
    {
        /// <summary>
        /// The Dual Grid Rule Tile will check if the contents of the data tile in that direction is filled.
        /// If not, the rule will fail.
        /// </summary>
        public const int Filled = 1;

        /// <summary>
        /// The Dual Grid Rule Tile will check if the contents of the data tile in that direction is not filled.
        /// If it is, the rule will fail.
        /// </summary>
        public const int NotFilled = 2;
    }

    /// <summary>
    /// Force sets the actual Data Tilemap before updating the tile, because Unity seems to move tiles between tilemaps sometimes.
    /// </summary>
    public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
    {
        SetDataTilemap(tilemap);

        var iden = Matrix4x4.identity;

        tileData.sprite = m_DefaultSprite;
        tileData.gameObject = m_DefaultGameObject;
        tileData.colliderType = m_DefaultColliderType;
        tileData.flags = TileFlags.LockTransform;
        tileData.transform = iden;

        bool gameObjectShouldBeInRenderTilemap = dualGridTilemapModule == null || dualGridTilemapModule.GameObjectOrigin == GameObjectOrigin.RenderTilemap;
        Matrix4x4 transform = iden;
        foreach (TilingRule rule in m_TilingRules)
        {
            if (RuleMatches(rule, position, tilemap, ref transform))
            {
                switch (rule.m_Output)
                {
                    case TilingRuleOutput.OutputSprite.Single:
                    case TilingRuleOutput.OutputSprite.Animation:
                        tileData.sprite = rule.m_Sprites[0];
                        break;
                    case TilingRuleOutput.OutputSprite.Random:
                        int index = Mathf.Clamp(Mathf.FloorToInt(GetPerlinValue(position, rule.m_PerlinScale, 100000f) * rule.m_Sprites.Length), 0, rule.m_Sprites.Length - 1);
                        tileData.sprite = rule.m_Sprites[index];
                        if (rule.m_RandomTransform != TilingRuleOutput.Transform.Fixed)
                            transform = ApplyRandomTransform(rule.m_RandomTransform, transform, rule.m_PerlinScale, position);
                        break;
                }
                tileData.transform = transform;
                tileData.gameObject = gameObjectShouldBeInRenderTilemap ? rule.m_GameObject : null;
                break;
            }
        }
    }

    /// <summary>
    /// Refreshes the <see cref="DataTile"/> with this <see cref="DualGridRuleTile"/>'s configuration.
    /// </summary>
    /// <returns>The refreshed data tile.</returns>
    public virtual DualGridDataTile RefreshDataTile()
    {
        if (dataTile == null) dataTile = ScriptableObject.CreateInstance<DualGridDataTile>();

        dataTile.colliderType = this.m_DefaultColliderType;
        dataTile.gameObject = this.m_DefaultGameObject;

        return dataTile;
    }

    /// <inheritdoc/>
    public override bool RuleMatches(TilingRule ruleToValidate, Vector3Int renderTilePosition, ITilemap tilemap, ref Matrix4x4 transform)
    {
        if (GetDataTilemap(tilemap) == null) return false;

        for (int corner = 0; corner < DualGridUtils.CornerCount; corner++)
        {
            Vector3Int dataTilePosition = DualGridUtils.GetDataTilePosition(renderTilePosition, corner);
            if (!DoesRuleMatchWithDataTile(ruleToValidate, dataTilePosition, renderTilePosition))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if the <paramref name="dataTilePosition"/> is filled in accordance with the defined <paramref name="rule"/>.
    /// </summary>
    private bool DoesRuleMatchWithDataTile(TilingRule rule, Vector3Int dataTilePosition, Vector3Int renderTilePosition)
    {
        Vector3Int dataTileOffset = dataTilePosition - renderTilePosition;

        int neighborIndex = rule.GetNeighborIndex(dataTileOffset);
        if (neighborIndex == -1) return true;

        TileBase neighborDataTile;
        if (dualGridTilemapModule != null)
        {
            neighborDataTile = dualGridTilemapModule.DataTileForRules(dataTilePosition);
        }
        else
        {
#if UNITY_EDITOR
            neighborDataTile = dataTilemap.GetEditorPreviewTile(dataTilePosition);
            if (neighborDataTile == null) neighborDataTile = dataTilemap.GetTile(dataTilePosition);
#else
            neighborDataTile = dataTilemap.GetTile(dataTilePosition);
#endif
        }

        return RuleMatch(rule.m_Neighbors[neighborIndex], neighborDataTile);
    }

    /// <inheritdoc/>
    public override bool RuleMatch(int neighbor, TileBase other)
    {
        bool isEmptyPreviewTile = other is DualGridPreviewTile dualGridPreviewTile && dualGridPreviewTile.IsFilled == false;

        return neighbor switch
        {
            DualGridNeighbor.Filled => !isEmptyPreviewTile && other != null,
            DualGridNeighbor.NotFilled => isEmptyPreviewTile || other == null,
            _ => true,
        };
    }

    /// <summary>
    /// Getter for the data tilemap, which can attempt to set it from the <paramref name="tilemap"/> if the <see cref="dataTilemap"/> field is <see langword="null"/>.
    /// <para></para>
    /// This is done because in key moments, the <see cref="StartUp"/> method has not yet been called, but the tile is being updated -> Unity messing this up and is not fixable externally.
    /// If the data tilemap would be null, the rule matching will not work properly.
    /// </summary>
    private Tilemap GetDataTilemap(ITilemap tilemap)
    {
        if (dualGridTilemapModule == null || dualGridTilemapModule.DataTilemap == null)
        {
            SetDataTilemap(tilemap);
        }

        return dataTilemap;
    }

    /// <summary>
    /// Finds the module of the render tilemap being updated. The last tilemap and its module are kept, so repeated
    /// updates on the same tilemap skip the parent search; a tilemap without a module leaves no data tilemap.
    /// </summary>
    private void SetDataTilemap(ITilemap tilemap)
    {
        Tilemap originTilemap = tilemap.GetComponent<Tilemap>();
        if (originTilemap != lastOriginTilemap || dualGridTilemapModule == null)
        {
            lastOriginTilemap = originTilemap;
            dualGridTilemapModule = originTilemap != null ? originTilemap.GetComponentInParent<DualGridTilemapModule>() : null;
        }

        dataTilemap = dualGridTilemapModule != null ? dualGridTilemapModule.DataTilemap : null;
    }
}
