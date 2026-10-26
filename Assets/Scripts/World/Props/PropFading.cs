using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Fades the tall props hiding this machine's local player (Docs/PropGeneration.md, In play), on this machine only:
/// other players never fade anything here. Each loaded chunk adds its fading props as it loads and takes them out as it
/// unloads (<see cref="ChunkPropFading"/>). They are kept in buckets by where they stand, so each frame looks only at the
/// few standing just in front of the player. A prop hides the player when it stands lower (so it draws over the
/// player) and its drawing covers enough of the player's body; it fades toward the settings' opacity while it does and
/// back once it does not. Props neither hiding the player nor fading back cost nothing per frame. Without settings it
/// never fades anything.
/// </summary>
public sealed class PropFading : ITickable
{
    private const float BucketSize = 4f;

    private readonly LocalPlayerTracker localPlayer;
    private readonly PropFadeSettings settings;
    private readonly Dictionary<Vector2Int, List<PropFade>> buckets = new();
    private readonly Dictionary<EntityId, Vector2Int> bucketOf = new();
    private readonly List<PropFade> shown = new();
    private readonly HashSet<PropFade> hiding = new();
    private float reachAcross;
    private float reachUp;

    /// <summary>
    /// Fades the props hiding <paramref name="localPlayerTracker"/>'s player as <paramref name="fadeSettings"/> say.
    /// </summary>
    public PropFading(LocalPlayerTracker localPlayerTracker, PropFadeSettings fadeSettings)
    {
        localPlayer = localPlayerTracker;
        settings = fadeSettings;
    }

    /// <summary>
    /// How many props are faded or fading back now.
    /// </summary>
    public int Shown => shown.Count;

    /// <summary>
    /// Adds the props of <paramref name="props"/> that fade, as their chunk loads.
    /// </summary>
    public void Add(IReadOnlyList<PropFade> props)
    {
        for (int i = 0; i < props.Count; i++)
        {
            PropFade prop = props[i];
            if (prop == null || !prop.Fades)
            {
                continue;
            }

            Vector2Int key = BucketOf(prop.transform.position);
            if (!bucketOf.TryAdd(prop.GetEntityId(), key))
            {
                continue;
            }

            if (!buckets.TryGetValue(key, out List<PropFade> bucket))
            {
                bucket = new List<PropFade>();
                buckets[key] = bucket;
            }

            bucket.Add(prop);
            Rect cover = prop.Cover;
            reachAcross = Mathf.Max(reachAcross, Mathf.Max(-cover.xMin, cover.xMax));
            reachUp = Mathf.Max(reachUp, cover.yMax);
        }
    }

    /// <summary>
    /// Takes the props of <paramref name="props"/> out as their chunk unloads, those already destroyed included, putting
    /// back the opacity of those faded.
    /// </summary>
    public void Remove(IReadOnlyList<PropFade> props)
    {
        for (int i = 0; i < props.Count; i++)
        {
            PropFade prop = props[i];
            if (ReferenceEquals(prop, null) || !bucketOf.Remove(prop.GetEntityId(), out Vector2Int key))
            {
                continue;
            }

            if (buckets.TryGetValue(key, out List<PropFade> bucket))
            {
                RemoveExactly(bucket, prop);
                if (bucket.Count == 0)
                {
                    buckets.Remove(key);
                }
            }

            if (RemoveExactly(shown, prop) && prop != null)
            {
                SetAlpha(prop.Visuals, 1f);
            }
        }
    }

    /// <summary>
    /// Fades the props over the local player, if any, for this frame.
    /// </summary>
    public void Tick()
    {
        LocalPlayer player = localPlayer.Current;
        Fade(player != null, player != null ? (Vector2)player.Transform.position : Vector2.zero, Time.deltaTime);
    }

    /// <summary>
    /// Fades, over <paramref name="deltaTime"/> seconds, the props whose drawing covers a body standing at
    /// <paramref name="feet"/> from in front of it, and fades the others back; with no <paramref name="viewer"/>,
    /// fades every prop back.
    /// </summary>
    internal void Fade(bool viewer, Vector2 feet, float deltaTime)
    {
        if (settings == null)
        {
            return;
        }

        hiding.Clear();
        if (viewer)
        {
            FindHiding(feet);
        }

        float step = deltaTime * (1f - settings.FadedAlpha) / settings.FadeSeconds;
        for (int i = shown.Count - 1; i >= 0; i--)
        {
            PropFade prop = shown[i];
            if (prop == null)
            {
                shown.RemoveAt(i);
                continue;
            }

            bool hides = hiding.Contains(prop);
            float alpha = Mathf.MoveTowards(prop.Visuals.color.a, hides ? settings.FadedAlpha : 1f, step);
            SetAlpha(prop.Visuals, alpha);
            if (!hides && alpha >= 1f)
            {
                shown.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Puts every prop that hides a body standing at <paramref name="feet"/> in <see cref="hiding"/>, and those not yet
    /// faded in <see cref="shown"/>.
    /// </summary>
    private void FindHiding(Vector2 feet)
    {
        Vector2 body = settings.Body;
        Rect bodyBox = new(feet.x - body.x * 0.5f, feet.y, body.x, body.y);
        float needed = body.x * body.y * settings.CoverShare;
        Vector2Int low = BucketOf(new Vector2(feet.x - body.x * 0.5f - reachAcross, feet.y - reachUp));
        Vector2Int high = BucketOf(new Vector2(feet.x + body.x * 0.5f + reachAcross, feet.y));
        for (int bx = low.x; bx <= high.x; bx++)
        {
            for (int by = low.y; by <= high.y; by++)
            {
                if (!buckets.TryGetValue(new Vector2Int(bx, by), out List<PropFade> bucket))
                {
                    continue;
                }

                for (int i = 0; i < bucket.Count; i++)
                {
                    PropFade prop = bucket[i];
                    if (prop == null || prop.transform.position.y >= feet.y || Overlap(prop.WorldCover, bodyBox) < needed)
                    {
                        continue;
                    }

                    if (hiding.Add(prop) && !shown.Contains(prop))
                    {
                        shown.Add(prop);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Takes <paramref name="prop"/> itself out of <paramref name="list"/>, and no other destroyed prop that compares
    /// equal to it, returning whether it was there.
    /// </summary>
    private static bool RemoveExactly(List<PropFade> list, PropFade prop)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], prop))
            {
                list.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The area <paramref name="a"/> and <paramref name="b"/> share.
    /// </summary>
    private static float Overlap(Rect a, Rect b)
    {
        float width = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
        float height = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
        return width > 0f && height > 0f ? width * height : 0f;
    }

    /// <summary>
    /// The bucket a prop standing at <paramref name="position"/> lies in.
    /// </summary>
    private static Vector2Int BucketOf(Vector2 position)
    {
        return new Vector2Int(Mathf.FloorToInt(position.x / BucketSize), Mathf.FloorToInt(position.y / BucketSize));
    }

    /// <summary>
    /// Gives <paramref name="renderer"/> the opacity <paramref name="alpha"/>, when it differs.
    /// </summary>
    private static void SetAlpha(SpriteRenderer renderer, float alpha)
    {
        Color colour = renderer.color;
        if (colour.a != alpha)
        {
            colour.a = alpha;
            renderer.color = colour;
        }
    }
}
