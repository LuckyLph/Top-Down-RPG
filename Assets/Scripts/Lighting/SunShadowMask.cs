using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// <summary>
/// One camera's sun shadow mask (Docs/Lighting.md, Phase 4): two textures of one texel per art pixel over an area of the
/// world aligned to its art grid, so the mask never moves against the ground as the camera does. The props' texture
/// holds the shadows of the static casters and is drawn again only when <see cref="PropsCurrent"/> turns false: the
/// view leaving the area (which is centred again on it, with a margin), the sun's shadow vector moving about
/// <c>redrawPixels</c> art pixels at the tip of the tallest caster's shadow, or <see cref="Invalidate"/>; the frame's
/// texture is the props' with the units' shadows drawn over it, every frame. The area covers the view, the feet of
/// casters standing up to the tallest caster's height below it, and the art pixels sprites read their shadow at.
/// <see cref="Prepare"/> also culls the casters whose shadows can reach the area, for the frame's draws.
/// </summary>
public sealed class SunShadowMask : IDisposable
{
    private const int ArtPixelsPerUnit = 32;
    private const int ReadPixels = 3;
    private const int SizeStep = 64;
    private const float CullDepth = 1000f;

    private readonly Plane[] planes = new Plane[6];
    private RTHandle props;
    private RTHandle frame;
    private RectInt area;
    private Vector2 drawnShadow;

    /// <summary>
    /// Creates the mask of <paramref name="viewer"/>, which draws nothing until prepared.
    /// </summary>
    public SunShadowMask(Camera viewer)
    {
        Camera = viewer;
    }

    public Camera Camera { get; }
    public RTHandle Props => props;
    public RTHandle Frame => frame;

    /// <summary>
    /// The area the mask covers, in art pixels of the world.
    /// </summary>
    public RectInt Area => area;

    /// <summary>
    /// Whether this frame draws the mask: the sun casts and the casters were culled.
    /// </summary>
    public bool IsCasting { get; private set; }

    /// <summary>
    /// Whether the props' texture holds their shadows for this frame's area and sun.
    /// </summary>
    public bool PropsCurrent { get; private set; }

    /// <summary>
    /// The sun's shadow vector this frame.
    /// </summary>
    public Vector2 Shadow { get; private set; }

    /// <summary>
    /// The casters whose shadows can reach the area this frame.
    /// </summary>
    public CullingResults Culling { get; private set; }

    /// <summary>
    /// The projection that draws world positions into the mask's texels.
    /// </summary>
    public Matrix4x4 DrawMatrix => GL.GetGPUProjectionMatrix(
        Matrix4x4.Ortho((float)area.xMin / ArtPixelsPerUnit, (float)area.xMax / ArtPixelsPerUnit, (float)area.yMin / ArtPixelsPerUnit, (float)area.yMax / ArtPixelsPerUnit, -CullDepth, CullDepth),
        true);

    /// <summary>
    /// The area as the shaders read it: its first art pixel and its size in art pixels.
    /// </summary>
    public Vector4 ShaderRect => new(area.xMin, area.yMin, area.width, area.height);

    /// <summary>
    /// Readies this frame's mask for a sun casting <paramref name="shadow"/> when <paramref name="casts"/>: centres the
    /// area again when the camera's view needs more than it covers, marks the props' shadows out of date when the area
    /// moved or the sun moved by <paramref name="redrawPixels"/> art pixels at the tip of a
    /// <paramref name="tallest"/> caster's shadow, and culls the casters whose shadows can reach the area. Draws
    /// nothing while the sun casts nothing or the camera is not orthographic.
    /// </summary>
    public void Prepare(ScriptableRenderContext context, Vector2 shadow, bool casts, float tallest, float margin, float redrawPixels)
    {
        IsCasting = Follow(shadow, casts, tallest, margin, redrawPixels) && Cull(context, shadow, tallest);
    }

    /// <summary>
    /// The part of <see cref="Prepare"/> that follows the camera and the sun without culling: whether the mask is drawn
    /// this frame, the area centred again and the props' shadows marked out of date as needed.
    /// </summary>
    internal bool Follow(Vector2 shadow, bool casts, float tallest, float margin, float redrawPixels)
    {
        if (!casts || !Camera.orthographic)
        {
            return false;
        }

        RectInt needed = Needed(tallest);
        if (props == null || !Contains(area, needed))
        {
            Centre(needed, Mathf.CeilToInt(margin * ArtPixelsPerUnit));
        }

        if ((shadow - drawnShadow).magnitude * tallest * ArtPixelsPerUnit >= redrawPixels)
        {
            PropsCurrent = false;
        }

        Shadow = shadow;
        return true;
    }

    /// <summary>
    /// Records that the props' texture now holds their shadows for this frame's sun.
    /// </summary>
    public void MarkPropsDrawn()
    {
        PropsCurrent = true;
        drawnShadow = Shadow;
    }

    /// <summary>
    /// Marks the props' shadows out of date, as when casters came or went.
    /// </summary>
    public void Invalidate()
    {
        PropsCurrent = false;
    }

    /// <summary>
    /// Releases the textures.
    /// </summary>
    public void Dispose()
    {
        Release();
    }

    /// <summary>
    /// The art pixels the camera's view needs: the view, the feet of casters up to <paramref name="tallest"/> units
    /// tall standing below it, and the art pixels sprites read their shadow at around them.
    /// </summary>
    private RectInt Needed(float tallest)
    {
        float halfHeight = Camera.orthographicSize;
        float halfWidth = halfHeight * Camera.aspect;
        Vector3 centre = Camera.transform.position;
        int xMin = Mathf.FloorToInt((centre.x - halfWidth) * ArtPixelsPerUnit) - ReadPixels;
        int xMax = Mathf.CeilToInt((centre.x + halfWidth) * ArtPixelsPerUnit) + ReadPixels;
        int yMin = Mathf.FloorToInt((centre.y - halfHeight - tallest) * ArtPixelsPerUnit) - ReadPixels;
        int yMax = Mathf.CeilToInt((centre.y + halfHeight) * ArtPixelsPerUnit) + ReadPixels;
        return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
    }

    /// <summary>
    /// Centres the area on <paramref name="needed"/> with <paramref name="marginPixels"/> around it, in a size rounded
    /// up to a step so a small change of the view keeps the textures, and marks the props' shadows out of date.
    /// </summary>
    private void Centre(RectInt needed, int marginPixels)
    {
        int width = RoundUp(needed.width + 2 * marginPixels);
        int height = RoundUp(needed.height + 2 * marginPixels);
        area = new RectInt(needed.x - (width - needed.width) / 2, needed.y - (height - needed.height) / 2, width, height);
        if (props == null || props.rt.width != width || props.rt.height != height)
        {
            Release();
            props = RTHandles.Alloc(width, height, GraphicsFormat.R8_UNorm, filterMode: FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: "SunShadowProps");
            frame = RTHandles.Alloc(width, height, GraphicsFormat.R8_UNorm, filterMode: FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: "SunShadowMask");
        }

        PropsCurrent = false;
    }

    /// <summary>
    /// Culls the casters standing where their shadows, up to the tallest caster's, can reach the area: the area and the
    /// area moved toward the sun by that shadow, and everything between.
    /// </summary>
    private bool Cull(ScriptableRenderContext context, Vector2 shadow, float tallest)
    {
        if (!Camera.TryGetCullingParameters(out ScriptableCullingParameters parameters))
        {
            return false;
        }

        Vector2 reach = -shadow * tallest;
        float xMin = (float)area.xMin / ArtPixelsPerUnit;
        float xMax = (float)area.xMax / ArtPixelsPerUnit;
        float yMin = (float)area.yMin / ArtPixelsPerUnit;
        float yMax = (float)area.yMax / ArtPixelsPerUnit;
        Matrix4x4 matrix = Matrix4x4.Ortho(
            Mathf.Min(xMin, xMin + reach.x),
            Mathf.Max(xMax, xMax + reach.x),
            Mathf.Min(yMin, yMin + reach.y),
            Mathf.Max(yMax, yMax + reach.y),
            -CullDepth,
            CullDepth);
        parameters.cullingMatrix = matrix;
        GeometryUtility.CalculateFrustumPlanes(matrix, planes);
        parameters.cullingPlaneCount = planes.Length;
        for (int i = 0; i < planes.Length; i++)
        {
            parameters.SetCullingPlane(i, planes[i]);
        }

        parameters.isOrthographic = true;
        parameters.cullingOptions = CullingOptions.ForceEvenIfCameraIsNotActive;
        Culling = context.Cull(ref parameters);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="outer"/> holds all of <paramref name="inner"/>.
    /// </summary>
    private static bool Contains(RectInt outer, RectInt inner)
    {
        return inner.xMin >= outer.xMin && inner.yMin >= outer.yMin && inner.xMax <= outer.xMax && inner.yMax <= outer.yMax;
    }

    /// <summary>
    /// <paramref name="pixels"/> rounded up to the size step.
    /// </summary>
    private static int RoundUp(int pixels)
    {
        return (pixels + SizeStep - 1) / SizeStep * SizeStep;
    }

    /// <summary>
    /// Releases the textures, if any.
    /// </summary>
    private void Release()
    {
        props?.Release();
        frame?.Release();
        props = null;
        frame = null;
    }
}
