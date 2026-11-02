using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tests for how sprites sort (Docs/WorldScale.md, Phase 1): by the 2D renderer's custom axis, at a constant order
/// within each sorting layer. Every prefab's renderers draw on their own layer, a group on another layer than the one
/// around it sorting at the root, and every renderer on the Entities layer draws at order 0, sorted by a sorting group
/// at its prefab's root or by its sprite's pivot on the root, but a character's lantern layer, at 1 inside its group,
/// over its body (Docs/Lighting.md, Phase 9). Rendered through the project's pipeline, the real prefabs
/// draw in the same order low in a world and in the sixteenth row of a 16-row world: a unit behind and in front of a
/// tree, an impact over the unit it hits and a death puff in front of a tree behind it.
/// </summary>
public class SpriteSortingTests
{
    private const string Entities = "Entities";
    private const string PlayerPath = "Assets/Prefabs/Player/Player.prefab";
    private const string WeaselPath = "Assets/Prefabs/Mobs/Weasel.prefab";
    private const string TreePath = "Assets/Prefabs/Props/Trees/Tree_Broadleaf_0.prefab";
    private const string ImpactPath = "Assets/Prefabs/Effects/Impact.prefab";
    private const string DeathPuffPath = "Assets/Prefabs/Effects/DeathPuff.prefab";
    private const int PixelsPerUnit = 32;
    private const int Width = 160;
    private const int Height = 192;
    private const float WorldX = 8196f;
    private const float FrameRise = 1.5f;
    private static readonly string[] PrefabFolders = { "Assets/Prefabs", "Assets/Dev" };
    private static readonly float[] Heights = { 10f, 1000f };
    private static readonly Color32 Background = new(255, 0, 255, 255);

    private readonly List<Object> created = new();

    /// <summary>
    /// Pairs of the game's sprites, one behind the other.
    /// </summary>
    public enum Pair
    {
        UnitBehindATree,
        UnitInFrontOfATree,
        ImpactOnAUnit,
        DeathPuffBeforeATree,
    }

    /// <summary>
    /// Destroys every object the test made.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object instance in created)
        {
            if (instance != null)
            {
                Object.DestroyImmediate(instance);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// Every renderer of every prefab under the prefab folders draws on its own sorting layer: in a sorting group, the
    /// group's layer is its own, and a group on another layer than the group around it sorts at the root. Every renderer
    /// on the Entities layer draws at order 0, sorted by a group at its prefab's root, on that layer at order 0, or by
    /// its sprite's pivot lying on the root; a character's lantern layer draws at 1 inside such a group, so it covers
    /// the body it is drawn for. No prefab holds a missing script.
    /// </summary>
    [Test]
    public void EveryPrefab_SortsOnTheEntitiesLayerAtOrder0_AsAGroupAtItsRootOrByItsPivot()
    {
        List<string> paths = AssetDatabase.FindAssets("t:Prefab", PrefabFolders).Select(AssetDatabase.GUIDToAssetPath).ToList();
        int sorted = 0;
        int grouped = 0;
        foreach (string path in paths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (Transform part in prefab.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(part.GetComponents<Component>().Any(c => c == null), Is.False, $"{path}: {part.name} holds a missing script.");
            }

            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                string name = $"{path}: {renderer.name}";
                SortingGroup group = renderer.GetComponentInParent<SortingGroup>(true);
                if (group != null)
                {
                    Assert.That(group.sortingLayerName, Is.EqualTo(renderer.sortingLayerName), $"{name} draws on its group's layer.");
                    SortingGroup outer = group.transform.parent != null ? group.transform.parent.GetComponentInParent<SortingGroup>(true) : null;
                    if (outer != null && outer.sortingLayerName != group.sortingLayerName)
                    {
                        Assert.That(group.sortAtRoot, Is.True, $"{name}'s group sorts at the root, or it draws on the {outer.sortingLayerName} layer.");
                    }
                }

                if (renderer.sortingLayerName != Entities)
                {
                    continue;
                }

                sorted++;
                CharacterVisuals character = renderer.GetComponentInParent<CharacterVisuals>(true);
                bool lantern = character != null && character.LanternRenderer == renderer;
                Assert.That(renderer.sortingOrder, Is.EqualTo(lantern ? 1 : 0), name);
                Assert.That(!lantern || group != null, Is.True, $"{name} draws over its body inside the character's group.");
                if (group != null)
                {
                    grouped++;
                    Assert.That(group.transform == prefab.transform, Is.True, $"{name} sorts as a group at its prefab's root.");
                    Assert.That(group.sortingOrder, Is.Zero, name);
                    Assert.That(group.sortAtRoot, Is.False, name);
                    continue;
                }

                SpriteRenderer sprite = renderer as SpriteRenderer;
                Assert.That(sprite != null && sprite.spriteSortPoint == SpriteSortPoint.Pivot, Is.True, $"{name} sorts by its sprite's pivot.");
                Assert.That(renderer.transform.position, Is.EqualTo(prefab.transform.position), $"{name}'s pivot lies on its prefab's root.");
            }
        }

        Assert.That(grouped, Is.GreaterThanOrEqualTo(6), "the units, the arrow and the effects");
        Assert.That(sorted, Is.GreaterThan(grouped + 50), "and the props");
    }

    /// <summary>
    /// The front one of <paramref name="pair"/> draws over the back one wherever they overlap, rendered through the
    /// project's pipeline at y 10 and at y 1000, the sixteenth row of a 16-row world, and the two renders are the same
    /// pixel for pixel.
    /// </summary>
    [TestCase(Pair.UnitBehindATree)]
    [TestCase(Pair.UnitInFrontOfATree)]
    [TestCase(Pair.ImpactOnAUnit)]
    [TestCase(Pair.DeathPuffBeforeATree)]
    public void TheFrontOne_DrawsOverTheBackOne_LowAndHighInA16RowWorld(Pair pair)
    {
        Color32[] low = null;
        foreach (float height in Heights)
        {
            Vector2 at = new(WorldX, height);
            Place(pair, at, out GameObject back, out GameObject front);
            Color32[] both = Render(at);
            front.SetActive(false);
            Color32[] backAlone = Render(at);
            front.SetActive(true);
            back.SetActive(false);
            Color32[] frontAlone = Render(at);
            back.SetActive(true);

            int asFront = 0;
            int asBack = 0;
            for (int i = 0; i < both.Length; i++)
            {
                if (frontAlone[i].Equals(Background) || backAlone[i].Equals(Background) || frontAlone[i].Equals(backAlone[i]))
                {
                    continue;
                }

                asFront += both[i].Equals(frontAlone[i]) ? 1 : 0;
                asBack += both[i].Equals(backAlone[i]) ? 1 : 0;
            }

            Assert.That(asFront, Is.GreaterThan(20), $"{pair} at y {height}: the two should overlap.");
            Assert.That(asBack, Is.Zero, $"{pair} at y {height}: the back one drew over the front one at {asBack} pixels.");
            if (low == null)
            {
                low = both;
            }
            else
            {
                Assert.That(both.SequenceEqual(low), Is.True, $"{pair} should draw the same at y {height} as at y {Heights[0]}.");
            }

            Object.DestroyImmediate(back);
            Object.DestroyImmediate(front);
        }
    }

    /// <summary>
    /// Places <paramref name="pair"/> around <paramref name="at"/> from the project's prefabs, the one behind as
    /// <paramref name="back"/> and the one in front as <paramref name="front"/>; an effect is played as the game plays
    /// it.
    /// </summary>
    private void Place(Pair pair, Vector2 at, out GameObject back, out GameObject front)
    {
        switch (pair)
        {
            case Pair.UnitBehindATree:
                back = Spawn(PlayerPath, at + Vector2.up * 0.5f);
                front = Spawn(TreePath, at);
                break;
            case Pair.UnitInFrontOfATree:
                back = Spawn(TreePath, at);
                front = Spawn(PlayerPath, at + Vector2.down * 0.5f);
                break;
            case Pair.ImpactOnAUnit:
                back = Spawn(WeaselPath, at);
                EffectPlayer impact = Spawn(ImpactPath, at).GetComponent<EffectPlayer>();
                float rise = back.GetComponent<HitFeedback>().ImpactHeight;
                impact.Play(0, impact.Sprites.DirectionOf(Vector2.right), at + new Vector2(-0.25f, rise), at.y, null);
                front = impact.gameObject;
                break;
            default:
                back = Spawn(TreePath, at + Vector2.up * 0.25f);
                EffectPlayer puff = Spawn(DeathPuffPath, at).GetComponent<EffectPlayer>();
                puff.Play(0, 0, at, at.y, null);
                front = puff.gameObject;
                break;
        }
    }

    /// <summary>
    /// An instance of the prefab at <paramref name="path"/> standing at <paramref name="at"/>.
    /// </summary>
    private GameObject Spawn(string path, Vector2 at)
    {
        GameObject instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), at, Quaternion.identity);
        created.Add(instance);
        return instance;
    }

    /// <summary>
    /// Renders <see cref="Width"/> by <see cref="Height"/> pixels at one screen pixel per art pixel around
    /// <paramref name="at"/>, raised so a tree there fits, through the project's pipeline over
    /// <see cref="Background"/>, with shaders compiled synchronously and every sorting group up to date.
    /// </summary>
    private Color32[] Render(Vector2 at)
    {
        GameObject cameraObject = new("SortingCamera");
        created.Add(cameraObject);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = Height * 0.5f / PixelsPerUnit;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Background;
        camera.transform.position = new Vector3(at.x, at.y + FrameRise, -10f);
        RenderTexture target = new(Width, Height, 0, RenderTextureFormat.ARGB32);
        Texture2D read = new(Width, Height, TextureFormat.RGBA32, false);
        bool allowAsync = ShaderUtil.allowAsyncCompilation;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            SortingGroup.UpdateAllSortingGroups();
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            RenderTexture.active = target;
            read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            read.Apply();
            return read.GetPixels32();
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = allowAsync;
            RenderTexture.active = null;
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(read);
            Object.DestroyImmediate(cameraObject);
        }
    }
}
