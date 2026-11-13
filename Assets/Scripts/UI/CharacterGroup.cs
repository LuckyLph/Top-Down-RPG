using System.Collections.Generic;

/// <summary>
/// What a row of the Character screen edits (Docs/UiRevamp.md, Phase 7): the look, the body's colours, or a part slot
/// with its colours.
/// </summary>
public enum CharacterGroupKind
{
    Look,
    Body,
    Part,
}

/// <summary>
/// A row of the Character screen: its kind, its label, the part slot it picks from (a part's row only) and the colour
/// choices it shows as chips, as indices of the <see cref="CharacterColours"/>, in their order there.
/// </summary>
public sealed class CharacterGroup
{
    /// <summary>
    /// Creates a group of <paramref name="kind"/> labelled <paramref name="label"/>, over <paramref name="slot"/> (null
    /// unless a part's) and the colour choices <paramref name="choices"/>.
    /// </summary>
    public CharacterGroup(CharacterGroupKind kind, string label, CharacterPartSlot slot, IReadOnlyList<int> choices)
    {
        Kind = kind;
        Label = label;
        Slot = slot;
        Choices = choices;
    }

    public CharacterGroupKind Kind { get; }
    public string Label { get; }
    public CharacterPartSlot Slot { get; }
    public IReadOnlyList<int> Choices { get; }
}
