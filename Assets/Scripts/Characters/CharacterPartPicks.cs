using System;
using Unity.Netcode;

/// <summary>
/// A player's parts as every machine names them (Docs/PlayerCharacter.md, Phase 6): for each <see cref="CharacterSlot"/>
/// the index of the option picked among its <see cref="CharacterParts"/>' options, the first, the look's own, by
/// default. A byte a slot, up to <see cref="MaxSlots"/>, so the value goes on the wire as it is.
/// </summary>
public struct CharacterPartPicks : IEquatable<CharacterPartPicks>, INetworkSerializeByMemcpy
{
    public const int MaxSlots = 16;
    public const int MaxOptions = 256;

    private ulong options;
    private ulong moreOptions;

    /// <summary>
    /// Whether every slot keeps its first option.
    /// </summary>
    public bool IsDefault => options == 0UL && moreOptions == 0UL;

    /// <summary>
    /// The option picked for <paramref name="slot"/>, 0 for its first.
    /// </summary>
    public int Get(CharacterSlot slot)
    {
        int index = (int)slot;
        if (index < 0 || index >= MaxSlots)
        {
            return 0;
        }

        ulong word = index < 8 ? options : moreOptions;
        return (int)((word >> (index % 8 * 8)) & 0xFFUL);
    }

    /// <summary>
    /// These picks with <paramref name="option"/> picked for <paramref name="slot"/>; unchanged for a slot past
    /// <see cref="MaxSlots"/> or an option outside a byte.
    /// </summary>
    public CharacterPartPicks With(CharacterSlot slot, int option)
    {
        int index = (int)slot;
        if (index < 0 || index >= MaxSlots || option < 0 || option >= MaxOptions)
        {
            return this;
        }

        int shift = index % 8 * 8;
        CharacterPartPicks picks = this;
        if (index < 8)
        {
            picks.options = (options & ~(0xFFUL << shift)) | ((ulong)option << shift);
        }
        else
        {
            picks.moreOptions = (moreOptions & ~(0xFFUL << shift)) | ((ulong)option << shift);
        }

        return picks;
    }

    /// <summary>
    /// Whether both pick the same option for every slot.
    /// </summary>
    public bool Equals(CharacterPartPicks other)
    {
        return options == other.options && moreOptions == other.moreOptions;
    }

    /// <inheritdoc />
    public override bool Equals(object obj)
    {
        return obj is CharacterPartPicks other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(options, moreOptions);
    }
}
