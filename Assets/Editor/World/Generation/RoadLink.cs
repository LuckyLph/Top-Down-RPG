using System;
using UnityEngine;

/// <summary>
/// A world road (Docs/WorldGeneration.md: Terms): from a region's port to another region's port, to the road leaving
/// another port, or to the nearest road already laid, of stone or gravel and a width. Links are laid in list order, so a
/// link that ends on a road comes after the link that lays it.
/// </summary>
[Serializable]
public sealed class RoadLink
{
    /// <summary>
    /// What a link's road is paved with: the world's stone or its gravel.
    /// </summary>
    public enum Surface
    {
        Stone,
        Gravel
    }

    [Tooltip("The key of the region whose port the road leaves.")]
    [SerializeField] private string fromRegion;
    [Tooltip("The side of that region its port faces.")]
    [SerializeField] private PortSide fromSide;
    [Tooltip("The key of the region the road leads to; empty to end on the nearest road already laid.")]
    [SerializeField] private string toRegion;
    [Tooltip("The side of that region the port the road leads to faces.")]
    [SerializeField] private PortSide toSide;
    [Tooltip("End on the road an earlier link laid to or from that port, wherever it is nearest, rather than at the port itself.")]
    [SerializeField] private bool toRoad;
    [Tooltip("What the road is paved with.")]
    [SerializeField] private Surface surface;
    [Tooltip("Width of the road in cells, at least as wide as the region roads it joins.")]
    [SerializeField, Min(1)] private int width = 3;

    /// <summary>
    /// Creates an empty link, for Unity serialization and the inspector.
    /// </summary>
    public RoadLink()
    {
    }

    /// <summary>
    /// Creates a link of <paramref name="roadWidth"/> cells of <paramref name="roadSurface"/> from
    /// <paramref name="fromKey"/>'s port on <paramref name="from"/> to <paramref name="toKey"/>'s port on
    /// <paramref name="to"/>, or with <paramref name="endOnRoad"/> to the road an earlier link laid to or from that
    /// port; an empty <paramref name="toKey"/> ends on the nearest road. For tests and tools.
    /// </summary>
    public RoadLink(string fromKey, PortSide from, string toKey, PortSide to, bool endOnRoad, Surface roadSurface, int roadWidth)
    {
        fromRegion = fromKey;
        fromSide = from;
        toRegion = toKey;
        toSide = to;
        toRoad = endOnRoad;
        surface = roadSurface;
        width = roadWidth;
    }

    public string FromRegion => fromRegion;
    public PortSide FromSide => fromSide;
    public string ToRegion => toRegion;
    public PortSide ToSide => toSide;
    public bool ToRoad => toRoad;
    public Surface RoadSurface => surface;
    public int Width => Mathf.Max(1, width);

    /// <summary>
    /// Whether the link ends on a road (the nearest, or one leaving its target port) rather than at a port.
    /// </summary>
    public bool EndsOnRoad => toRoad || string.IsNullOrEmpty(toRegion);
}
