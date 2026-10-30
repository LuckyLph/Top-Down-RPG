using System;
using UnityEngine;

/// <summary>
/// One frame of a spark star impact (<see cref="ImpactEffectRecipe"/>): a hot core, and rays out from the middle, one
/// along the hit and the rest evenly around it, every other one long. Lengths are in pixels at scale 1.
/// </summary>
[Serializable]
public struct ImpactStarFrame
{
    [SerializeField, Min(0f), Tooltip("The hot core's radius; 0 for none.")]
    private float coreRadius;
    [SerializeField, Min(0f), Tooltip("Where rays start, from the middle.")]
    private float rayFrom;
    [SerializeField, Min(0f), Tooltip("How far the long rays reach.")]
    private float longRayTip;
    [SerializeField, Min(0f), Tooltip("How far the short rays reach.")]
    private float shortRayTip;
    [SerializeField, Min(0f), Tooltip("How much of each ray is drawn, back from its tip: 0 draws a dot at the tip, more than its length the whole ray.")]
    private float rayLength;
    [SerializeField, Min(0f), Tooltip("How far back from the tip the hot inner part of each ray stops; 0 draws none.")]
    private float hotBack;
    [SerializeField, Tooltip("The palette colour the rays take.")]
    private ImpactShade rayShade;

    /// <summary>
    /// Creates a frame of the given core and rays.
    /// </summary>
    public ImpactStarFrame(float coreRadius, float rayFrom, float longRayTip, float shortRayTip, float rayLength, float hotBack, ImpactShade rayShade)
    {
        this.coreRadius = coreRadius;
        this.rayFrom = rayFrom;
        this.longRayTip = longRayTip;
        this.shortRayTip = shortRayTip;
        this.rayLength = rayLength;
        this.hotBack = hotBack;
        this.rayShade = rayShade;
    }

    public float CoreRadius => coreRadius;
    public float RayFrom => rayFrom;
    public float LongRayTip => longRayTip;
    public float ShortRayTip => shortRayTip;
    public float RayLength => rayLength;
    public float HotBack => hotBack;
    public ImpactShade RayShade => rayShade;
}

/// <summary>
/// The colours of an impact palette, hottest first.
/// </summary>
public enum ImpactShade
{
    Hot,
    Light,
    Middle,
    Dark
}

/// <summary>
/// An impact's colours for one damage type, hottest first; its outline is the darkest colour darkened further. An
/// impact that casts light lights what is around it in its middle colour (Docs/Lighting.md, Phase 2).
/// </summary>
[Serializable]
public struct ImpactPalette
{
    [SerializeField] private DamageType type;
    [SerializeField] private Color32 hot;
    [SerializeField] private Color32 light;
    [SerializeField] private Color32 middle;
    [SerializeField] private Color32 dark;
    [Tooltip("Whether the impact lights what is around it, in its middle colour.")]
    [SerializeField] private bool castsLight;

    /// <summary>
    /// Creates the palette of <paramref name="damageType"/>, casting light in its middle colour when
    /// <paramref name="lights"/>.
    /// </summary>
    public ImpactPalette(DamageType damageType, Color32 hot, Color32 light, Color32 middle, Color32 dark, bool lights = false)
    {
        type = damageType;
        this.hot = hot;
        this.light = light;
        this.middle = middle;
        this.dark = dark;
        castsLight = lights;
    }

    public DamageType Type => type;
    public bool CastsLight => castsLight;

    /// <summary>
    /// The colour of <paramref name="shade"/>.
    /// </summary>
    public Color32 Get(ImpactShade shade)
    {
        switch (shade)
        {
            case ImpactShade.Hot:
                return hot;
            case ImpactShade.Light:
                return light;
            case ImpactShade.Middle:
                return middle;
            default:
                return dark;
        }
    }
}

/// <summary>
/// One round blob of a death puff (<see cref="PuffEffectRecipe"/>): where it sits above the feet, its radius in
/// pixels and when it appears, in seconds.
/// </summary>
[Serializable]
public struct PuffBlob
{
    [SerializeField] private Vector2 position;
    [SerializeField, Min(0f)] private float radius;
    [SerializeField, Min(0f)] private float delay;

    /// <summary>
    /// Creates a blob at <paramref name="position"/> pixels from the feet, of <paramref name="radius"/>, appearing
    /// <paramref name="delay"/> seconds in.
    /// </summary>
    public PuffBlob(Vector2 position, float radius, float delay)
    {
        this.position = position;
        this.radius = radius;
        this.delay = delay;
    }

    public Vector2 Position => position;
    public float Radius => radius;
    public float Delay => delay;
}
