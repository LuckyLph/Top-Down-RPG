using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Composes players' outfits and keeps them while they are worn (Docs/PlayerCharacter.md, Phases 6 and 7).
/// <see cref="Wear"/> gives the set a look's parts show as a player picked them: the look's own set for its own parts,
/// else an outfit composed from the look's layers (<see cref="CharacterComposer"/>) the first time those picks are
/// asked for, and shared by every player wearing them. Its composition starts on the next frame, if anyone still wears
/// it then, and runs off the main thread, its atlas packed there too; back on the main thread its texture is made on
/// one frame and its sprites a few hundred a frame (<c>spritesPerFrame</c>), so no frame takes the whole build. A wearer
/// lets go of its outfit through its <see cref="CharacterOutfitLease"/>; when the last lets go, the outfit is kept as a
/// spare, the most recent <c>spareOutfits</c> of them, so switching back (a weapon's gear, a part tried and put back)
/// shows it at once, and the oldest spare beyond them is destroyed; a composition still under way is dropped. Gameplay
/// scope; disposing destroys every outfit.
/// </summary>
public sealed class CharacterOutfits : IDisposable
{
    private readonly Dictionary<(CharacterParts Parts, CharacterPartPicks Picks), Entry> outfits = new();
    private readonly List<(CharacterParts Parts, CharacterPartPicks Picks)> spares = new();
    private readonly int spareOutfits;
    private readonly int spritesPerFrame;
    private bool disposed;

    /// <summary>
    /// Keeps up to <paramref name="spareOutfits"/> outfits nobody wears, and builds an outfit's sprites
    /// <paramref name="spritesPerFrame"/> a frame in play.
    /// </summary>
    public CharacterOutfits(int spareOutfits, int spritesPerFrame)
    {
        this.spareOutfits = Mathf.Max(0, spareOutfits);
        this.spritesPerFrame = Mathf.Max(1, spritesPerFrame);
    }

    /// <summary>
    /// How many outfits are composed, being composed or kept as spares.
    /// </summary>
    internal int Count => outfits.Count;

    /// <summary>
    /// How many composed outfits nobody wears are kept.
    /// </summary>
    internal int SpareCount => spares.Count;

    /// <summary>
    /// Destroys every outfit and drops every composition under way.
    /// </summary>
    public void Dispose()
    {
        disposed = true;
        foreach (Entry entry in outfits.Values)
        {
            entry.Outfit?.Destroy();
            entry.Waiting.Clear();
        }

        outfits.Clear();
        spares.Clear();
    }

    /// <summary>
    /// Wears the outfit <paramref name="picks"/> pick of <paramref name="parts"/>: <paramref name="ready"/> gets its set,
    /// at once when it is the look's own, already composed or kept as a spare, else on the main thread once composed
    /// (never once the lease is released).
    /// </summary>
    public CharacterOutfitLease Wear(CharacterParts parts, CharacterPartPicks picks, Action<CharacterAnimationSet> ready)
    {
        return Wear(parts, picks, ready, false);
    }

    /// <summary>
    /// Wears the outfit <paramref name="picks"/> pick of <paramref name="parts"/>, composing it on this thread when it is
    /// not yet composed, for tests and tools.
    /// </summary>
    internal CharacterOutfitLease WearNow(CharacterParts parts, CharacterPartPicks picks)
    {
        return Wear(parts, picks, null, true);
    }

    /// <summary>
    /// Lets <paramref name="lease"/>'s wearer go of its outfit; when it was the last, a composed outfit becomes the
    /// newest spare (the oldest spare past the most kept is destroyed) and a composition under way is dropped.
    /// </summary>
    internal void Release(CharacterOutfitLease lease)
    {
        if (!outfits.TryGetValue(lease.Key, out Entry entry))
        {
            return;
        }

        entry.Waiting.Remove(lease);
        entry.Wearers--;
        if (entry.Wearers > 0)
        {
            return;
        }

        if (entry.Outfit == null || spareOutfits == 0)
        {
            entry.Outfit?.Destroy();
            outfits.Remove(lease.Key);
            return;
        }

        spares.Add(lease.Key);
        while (spares.Count > spareOutfits)
        {
            (CharacterParts, CharacterPartPicks) oldest = spares[0];
            spares.RemoveAt(0);
            outfits[oldest].Outfit.Destroy();
            outfits.Remove(oldest);
        }
    }

    /// <summary>
    /// Wears an outfit, composing it at once when <paramref name="now"/>.
    /// </summary>
    private CharacterOutfitLease Wear(CharacterParts parts, CharacterPartPicks picks, Action<CharacterAnimationSet> ready, bool now)
    {
        if (parts == null || parts.IsOwn(picks))
        {
            CharacterAnimationSet own = parts != null ? parts.Look : null;
            ready?.Invoke(own);
            return new CharacterOutfitLease(this, default, null, own);
        }

        (CharacterParts, CharacterPartPicks) key = (parts, Normalized(parts, picks));
        if (!outfits.TryGetValue(key, out Entry entry))
        {
            entry = new Entry();
            outfits.Add(key, entry);
            if (now)
            {
                entry.Outfit = CharacterComposer.Build(Compose(Bytes(parts, key.Item2), parts.InnerLineGap), parts.Look, parts.DyesOf(key.Item2), OutfitName(parts, key.Item2));
            }
            else
            {
                ComposeLater(key, entry);
            }
        }
        else if (entry.Wearers == 0)
        {
            spares.Remove(key);
        }

        entry.Wearers++;
        CharacterOutfitLease lease = new(this, key, ready, entry.Outfit?.Set);
        if (entry.Outfit != null)
        {
            ready?.Invoke(entry.Outfit.Set);
        }
        else
        {
            entry.Waiting.Add(lease);
        }

        return lease;
    }

    /// <summary>
    /// From the next frame in play, if anyone still wears <paramref name="entry"/>: composes its outfit and packs its
    /// atlas off the main thread, then builds it on the main thread, its sprites <c>spritesPerFrame</c> a frame, and
    /// hands it to its waiting wearers; dropped when everyone let go meanwhile or the service was disposed.
    /// </summary>
    private async void ComposeLater((CharacterParts Parts, CharacterPartPicks Picks) key, Entry entry)
    {
        if (Application.isPlaying)
        {
            await Awaitable.NextFrameAsync();
            if (!Wanted(key, entry))
            {
                return;
            }
        }

        byte[][] bytes = Bytes(key.Parts, key.Picks);
        float gap = key.Parts.InnerLineGap;
        CharacterOutfitAtlas atlas;
        try
        {
            await Awaitable.BackgroundThreadAsync();
            atlas = CharacterComposer.Pack(Compose(bytes, gap));
        }
        catch (Exception exception)
        {
            await Awaitable.MainThreadAsync();
            Debug.LogException(exception);
            return;
        }

        await Awaitable.MainThreadAsync();
        if (!Wanted(key, entry))
        {
            return;
        }

        CharacterOutfitBuild build = CharacterComposer.BeginBuild(atlas, key.Parts.Look, key.Parts.DyesOf(key.Picks), OutfitName(key.Parts, key.Picks));
        while (Application.isPlaying && !build.IsDone)
        {
            await Awaitable.NextFrameAsync();
            if (!Wanted(key, entry))
            {
                build.Abort();
                return;
            }

            build.Step(spritesPerFrame);
        }

        build.Step(int.MaxValue);
        entry.Outfit = build.Finish();
        CharacterOutfitLease[] waiting = entry.Waiting.ToArray();
        entry.Waiting.Clear();
        foreach (CharacterOutfitLease lease in waiting)
        {
            lease.Deliver(entry.Outfit.Set);
        }
    }

    /// <summary>
    /// Whether <paramref name="entry"/> is still the outfit under <paramref name="key"/> and someone wears it.
    /// </summary>
    private bool Wanted((CharacterParts Parts, CharacterPartPicks Picks) key, Entry entry)
    {
        return !disposed && outfits.TryGetValue(key, out Entry current) && current == entry && entry.Wearers > 0;
    }

    /// <summary>
    /// The bytes of the body's layer, then of each slot's layer the picks lay, in <see cref="CharacterSlot"/> order
    /// (null where none), read on the main thread.
    /// </summary>
    private static byte[][] Bytes(CharacterParts parts, CharacterPartPicks picks)
    {
        CharacterSlot[] slots = (CharacterSlot[])Enum.GetValues(typeof(CharacterSlot));
        byte[][] bytes = new byte[slots.Length + 1][];
        bytes[0] = parts.Body.bytes;
        for (int i = 0; i < slots.Length; i++)
        {
            TextAsset layer = parts.LayerOf(picks, slots[i]);
            bytes[i + 1] = layer != null ? layer.bytes : null;
        }

        return bytes;
    }

    /// <summary>
    /// Reads the layers and composes every frame of the outfit.
    /// </summary>
    private static CharacterOutfitFrames Compose(byte[][] bytes, float gap)
    {
        CharacterLayer body = CharacterLayer.Read(bytes[0]);
        CharacterLayer[] layers = new CharacterLayer[bytes.Length - 1];
        for (int i = 0; i < layers.Length; i++)
        {
            layers[i] = bytes[i + 1] != null ? CharacterLayer.Read(bytes[i + 1]) : null;
        }

        return new CharacterComposer().ComposeAll(body, layers, gap);
    }

    /// <summary>
    /// <paramref name="picks"/> with every pick the parts lack back to the first option, so picks showing the same
    /// outfit share it.
    /// </summary>
    private static CharacterPartPicks Normalized(CharacterParts parts, CharacterPartPicks picks)
    {
        CharacterPartPicks shown = default;
        foreach (CharacterSlot slot in (CharacterSlot[])Enum.GetValues(typeof(CharacterSlot)))
        {
            shown = shown.With(slot, Mathf.Max(0, parts.Shown(picks, slot)));
        }

        return shown;
    }

    /// <summary>
    /// The outfit's name: the parts' and each slot's option.
    /// </summary>
    private static string OutfitName(CharacterParts parts, CharacterPartPicks picks)
    {
        System.Text.StringBuilder name = new(parts.name);
        foreach (CharacterSlot slot in (CharacterSlot[])Enum.GetValues(typeof(CharacterSlot)))
        {
            int option = parts.Shown(picks, slot);
            name.Append(' ').Append(option >= 0 ? parts.Get(slot).Options[option].Name : "-");
        }

        return name.ToString();
    }

    /// <summary>
    /// An outfit composed or being composed, how many wear it and who waits for it.
    /// </summary>
    private sealed class Entry
    {
        public CharacterOutfit Outfit;
        public int Wearers;
        public readonly List<CharacterOutfitLease> Waiting = new();
    }
}

/// <summary>
/// A wearer's hold on an outfit of <see cref="CharacterOutfits"/>: its set once ready, and <see cref="Release"/> to let
/// go of it, once.
/// </summary>
public sealed class CharacterOutfitLease
{
    private readonly CharacterOutfits outfits;
    private readonly Action<CharacterAnimationSet> ready;
    private bool released;

    /// <summary>
    /// Creates a lease of the outfit under <paramref name="key"/> (none for a look's own set, which needs no letting go).
    /// </summary>
    internal CharacterOutfitLease(CharacterOutfits owner, (CharacterParts Parts, CharacterPartPicks Picks) key, Action<CharacterAnimationSet> onReady, CharacterAnimationSet set)
    {
        outfits = owner;
        Key = key;
        ready = onReady;
        Set = set;
    }

    internal (CharacterParts Parts, CharacterPartPicks Picks) Key { get; }

    /// <summary>
    /// The outfit's set once ready; null while it is composed.
    /// </summary>
    public CharacterAnimationSet Set { get; private set; }

    /// <summary>
    /// Lets go of the outfit, once; its set is not delivered after.
    /// </summary>
    public void Release()
    {
        if (released)
        {
            return;
        }

        released = true;
        if (Key.Parts != null)
        {
            outfits.Release(this);
        }
    }

    /// <summary>
    /// Hands the composed set to the wearer, unless it let go.
    /// </summary>
    internal void Deliver(CharacterAnimationSet set)
    {
        if (released)
        {
            return;
        }

        Set = set;
        ready?.Invoke(set);
    }
}
