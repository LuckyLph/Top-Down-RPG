using System;
using Unity.Netcode;

/// <summary>
/// A player's colours as every machine names them (Docs/PlayerCharacter.md, Phase 5): for each choice of the
/// <see cref="CharacterColours"/>, by its index there, the swatch picked from the choice's list, or none for the
/// choice's own swatch, the colours the look was baked in. A byte a choice, up to <see cref="MaxChoices"/>, so the
/// value goes on the wire as it is; the default value picks every choice's own.
/// </summary>
public struct CharacterColourPicks : IEquatable<CharacterColourPicks>, INetworkSerializeByMemcpy
{
    public const int MaxChoices = 16;
    public const int MaxSwatches = 255;

    private ulong low;
    private ulong high;

    /// <summary>
    /// Whether every choice keeps its own swatch.
    /// </summary>
    public bool IsDefault => low == 0UL && high == 0UL;

    /// <summary>
    /// The swatch picked for <paramref name="choice"/>, its index in the choice's list, or -1 for the choice's own (also
    /// for a choice past <see cref="MaxChoices"/>).
    /// </summary>
    public int Get(int choice)
    {
        if (choice < 0 || choice >= MaxChoices)
        {
            return -1;
        }

        ulong word = choice < 8 ? low : high;
        return (int)((word >> (choice % 8 * 8)) & 0xFFUL) - 1;
    }

    /// <summary>
    /// These picks with <paramref name="swatch"/> (an index in the choice's list, or -1 for its own) picked for
    /// <paramref name="choice"/>; unchanged for a choice past <see cref="MaxChoices"/> or a swatch past
    /// <see cref="MaxSwatches"/>.
    /// </summary>
    public CharacterColourPicks With(int choice, int swatch)
    {
        if (choice < 0 || choice >= MaxChoices || swatch < -1 || swatch >= MaxSwatches)
        {
            return this;
        }

        int shift = choice % 8 * 8;
        ulong mask = ~(0xFFUL << shift);
        ulong value = (ulong)(swatch + 1) << shift;
        CharacterColourPicks picks = this;
        if (choice < 8)
        {
            picks.low = (low & mask) | value;
        }
        else
        {
            picks.high = (high & mask) | value;
        }

        return picks;
    }

    /// <summary>
    /// Whether both pick the same swatch for every choice.
    /// </summary>
    public bool Equals(CharacterColourPicks other)
    {
        return low == other.low && high == other.high;
    }

    /// <inheritdoc />
    public override bool Equals(object obj)
    {
        return obj is CharacterColourPicks other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(low, high);
    }
}
