using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for the shared canvas's prop primitives and shading (Docs/PropGeneration.md, The canvas): an ellipsoid with
/// equal axes covers what a sphere covers, facing the same ways; a convex cube shows its top and front faces with their
/// own normals; a marched field of a sphere matches the sphere; the ground hides what lies under it; a cover is taken
/// only where the surface faces up past its threshold; and a tone offset darkens by one tone. The characters' frames
/// staying byte for byte the same is checked by the character, Weasel and weapon tests against the project's sheets.
/// </summary>
public class ArtCanvasTests
{
    private const int Size = 48;
    private const float Scale = 10f;
    private static readonly Vector2Int Pivot = new(Size / 2, 12);
    private static readonly ColourRamp Red = new(new Color32(40, 0, 0, 255), new Color32(100, 0, 0, 255), new Color32(160, 0, 0, 255), new Color32(220, 0, 0, 255));
    private static readonly ColourRamp Blue = new(new Color32(0, 0, 40, 255), new Color32(0, 0, 100, 255), new Color32(0, 0, 160, 255), new Color32(0, 0, 220, 255));

    /// <summary>
    /// An ellipsoid whose three half axes are one radius long fills exactly the sphere's pixels, with normals within a
    /// degree of the sphere's.
    /// </summary>
    [Test]
    public void Ellipsoid_WithEqualAxes_CoversWhatASphereCovers()
    {
        Vector3 centre = new(0f, 0f, 1.2f);
        ArtCanvas sphere = Canvas();
        sphere.Sphere(centre, 1f, 0, 0);
        ArtCanvas ellipsoid = Canvas();
        ellipsoid.Ellipsoid(centre, new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f), 0, 0);

        int filled = 0;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Assert.That(ellipsoid.IsFilled(x, y), Is.EqualTo(sphere.IsFilled(x, y)), $"pixel {x}, {y}");
                if (sphere.IsFilled(x, y))
                {
                    filled++;
                    Assert.That(Vector3.Dot(ellipsoid.NormalAt(x, y), sphere.NormalAt(x, y)), Is.GreaterThan(0.9998f), $"pixel {x}, {y}");
                }
            }
        }

        Assert.That(filled, Is.GreaterThan(200));
    }

    /// <summary>
    /// A cube cut from six planes shows its top facing up near its top and its front facing south near its bottom,
    /// each pixel with the face's normal and a world position on that face.
    /// </summary>
    [Test]
    public void Convex_Cube_ShowsItsTopAndFrontFacesWithTheirNormals()
    {
        ArtCanvas canvas = Canvas();
        Vector4[] planes =
        {
            new(1f, 0f, 0f, 1f), new(-1f, 0f, 0f, 1f), new(0f, 1f, 0f, 1f), new(0f, -1f, 0f, 1f), new(0f, 0f, 1f, 2f), new(0f, 0f, -1f, 0f)
        };
        canvas.Convex(planes, new Bounds(new Vector3(0f, 0f, 1f), new Vector3(2f, 2f, 2f)), 0, 0);

        Vector2Int top = canvas.PixelOf(new Vector3(0f, 0f, 2f));
        Vector2Int front = canvas.PixelOf(new Vector3(0f, -1f, 0.5f));
        Assert.That(canvas.IsFilled(top.x, top.y), Is.True);
        Assert.That(canvas.IsFilled(front.x, front.y), Is.True);
        Assert.That(canvas.WorldNormalAt(top.x, top.y), Is.EqualTo(new Vector3(0f, 0f, 1f)));
        Assert.That(canvas.WorldNormalAt(front.x, front.y), Is.EqualTo(new Vector3(0f, -1f, 0f)));
        Assert.That(canvas.WorldPositionAt(top.x, top.y).z, Is.EqualTo(2f).Within(0.01f));
        Assert.That(canvas.WorldPositionAt(front.x, front.y).y, Is.EqualTo(-1f).Within(0.01f));
    }

    /// <summary>
    /// A field below zero inside a sphere, marched along the view rays, fills the sphere's pixels but for a few on its
    /// rim, with normals close to the sphere's.
    /// </summary>
    [Test]
    public void Field_OfASphere_MatchesTheSphere()
    {
        Vector3 centre = new(0f, 0f, 1.2f);
        ArtCanvas sphere = Canvas();
        sphere.Sphere(centre, 1f, 0, 0);
        ArtCanvas field = Canvas();
        field.Field(p => (p - centre).magnitude - 1f, new Bounds(centre, Vector3.one * 2.2f), (p, n) => 0, 0);

        int filled = 0, differing = 0;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (sphere.IsFilled(x, y) != field.IsFilled(x, y))
                {
                    differing++;
                    continue;
                }

                if (sphere.IsFilled(x, y))
                {
                    filled++;
                    Assert.That(Vector3.Dot(field.NormalAt(x, y), sphere.NormalAt(x, y)), Is.GreaterThan(0.98f), $"pixel {x}, {y}");
                }
            }
        }

        Assert.That(filled, Is.GreaterThan(200));
        Assert.That(differing, Is.LessThanOrEqualTo(8));
    }

    /// <summary>
    /// With the ground hiding what lies under it, a sphere half in the ground shows only its upper half: no pixel's
    /// surface under the ground, and fewer pixels than without.
    /// </summary>
    [Test]
    public void HidesBelowGround_HidesWhatLiesUnderTheGround()
    {
        ArtCanvas open = Canvas();
        open.Sphere(Vector3.zero, 1f, 0, 0);
        ArtCanvas sunk = Canvas();
        sunk.HidesBelowGround = true;
        sunk.Sphere(Vector3.zero, 1f, 0, 0);

        int shown = 0, hidden = 0;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (sunk.IsFilled(x, y))
                {
                    shown++;
                    Assert.That(sunk.WorldPositionAt(x, y).z, Is.GreaterThanOrEqualTo(0f), $"pixel {x}, {y}");
                }
                else if (open.IsFilled(x, y))
                {
                    hidden++;
                }
            }
        }

        Assert.That(shown, Is.GreaterThan(0));
        Assert.That(hidden, Is.GreaterThan(0));
    }

    /// <summary>
    /// A material with a cover and no noise takes the cover's colours exactly where its world normal faces up past the
    /// threshold, and its own elsewhere.
    /// </summary>
    [Test]
    public void Shade_Cover_IsTakenWhereTheSurfaceFacesUpPastItsThreshold()
    {
        ArtCanvas canvas = Canvas();
        canvas.Sphere(new Vector3(0f, 0f, 1.2f), 1f, 0, 0);
        ArtMaterial[] table = { new(Red, cover: 1, coverThreshold: 0.7f), new(Blue) };
        Color32[] pixels = canvas.Shade(table, 0.64f, 0.28f, 2.6f);

        int covered = 0, bare = 0;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (!canvas.IsFilled(x, y) || IsOutline(pixels[y * Size + x]))
                {
                    continue;
                }

                Color32 colour = pixels[y * Size + x];
                bool up = canvas.WorldNormalAt(x, y).z > 0.7f;
                Assert.That(colour.b > 0, Is.EqualTo(up), $"pixel {x}, {y}");
                Assert.That(colour.r > 0, Is.EqualTo(!up), $"pixel {x}, {y}");
                covered += up ? 1 : 0;
                bare += up ? 0 : 1;
            }
        }

        Assert.That(covered, Is.GreaterThan(0));
        Assert.That(bare, Is.GreaterThan(0));
    }

    /// <summary>
    /// A tone offset of -1 draws each pixel one tone darker than without, the shadow tone staying the darkest.
    /// </summary>
    [Test]
    public void Shade_ToneOffset_DarkensByOneTone()
    {
        ArtCanvas canvas = Canvas();
        canvas.Sphere(new Vector3(0f, 0f, 1.2f), 1f, 0, 0);
        Color32[] plain = canvas.Shade(new[] { new ArtMaterial(Red) }, 0.64f, 0.28f, 2.6f);
        Color32[] darker = canvas.Shade(new[] { new ArtMaterial(Red, toneOffset: -1) }, 0.64f, 0.28f, 2.6f);

        bool sawLight = false;
        for (int i = 0; i < plain.Length; i++)
        {
            if (plain[i].a == 0 || IsOutline(plain[i]))
            {
                continue;
            }

            byte expected = plain[i].r == Red.Light.r ? Red.Middle.r : Red.Shadow.r;
            sawLight |= plain[i].r == Red.Light.r;
            Assert.That(darker[i].r, Is.EqualTo(expected), $"pixel {i}");
        }

        Assert.That(sawLight, Is.True);
    }

    /// <summary>
    /// An empty canvas of <see cref="Size"/> pixels a side at <see cref="Scale"/> pixels a unit.
    /// </summary>
    private static ArtCanvas Canvas()
    {
        return new ArtCanvas(Size, Size, Pivot, Scale);
    }

    /// <summary>
    /// Whether <paramref name="colour"/> is one of the test ramps' outline colours.
    /// </summary>
    private static bool IsOutline(Color32 colour)
    {
        return (colour.r == Red.Outline.r && colour.b == 0) || (colour.b == Blue.Outline.b && colour.r == 0);
    }
}
