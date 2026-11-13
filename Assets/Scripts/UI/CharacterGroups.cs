using System.Collections.Generic;

/// <summary>
/// The Character screen's rows (Docs/UiRevamp.md, Phase 7), read from the dyes rather than a hand-made list: the Look
/// row when the catalog has more than one look; then, over the first look that wears parts, a Body row and a row per
/// part slot a player picks (every slot but the weapon's, which shows the equipped weapon's gear). A colour choice a
/// look of the catalog carries goes on the one part slot's row whose options carry its dyes, and on the Body row when
/// no slot or several do: the skin, which tops, legs and bare hands all show, stays the body's.
/// </summary>
public static class CharacterGroups
{
    public const string LookLabel = "Look";
    public const string BodyLabel = "Body";

    /// <summary>
    /// The rows for <paramref name="looks"/> and <paramref name="colours"/>, in order: the Look row, the Body row when it
    /// has colours, then each part slot's with more than one option or with colours, in slot order. Only the Look row
    /// without a look that wears parts.
    /// </summary>
    public static List<CharacterGroup> Build(CharacterLookCatalog looks, CharacterColours colours)
    {
        List<CharacterGroup> groups = new();
        if (looks == null)
        {
            return groups;
        }

        CharacterParts parts = PartsOf(looks);
        int carried = 0;
        for (int i = 0; i < looks.Count; i++)
        {
            CharacterLook look = looks.Get(i);
            carried |= look != null && look.AnimationSet != null ? look.AnimationSet.Dyes : 0;
            carried |= look != null && look.Parts != null ? look.Parts.Dyes : 0;
        }

        if (looks.Count >= 2)
        {
            groups.Add(new CharacterGroup(CharacterGroupKind.Look, LookLabel, null, new int[0]));
        }

        if (parts == null)
        {
            return groups;
        }

        List<CharacterPartSlot> slots = new();
        foreach (CharacterPartSlot slot in parts.Slots)
        {
            if (slot != null && !slot.FollowsWeapon)
            {
                slots.Add(slot);
            }
        }

        List<int> body = new();
        Dictionary<CharacterPartSlot, List<int>> bySlot = new();
        for (int choice = 0; colours != null && choice < colours.Count && choice < CharacterColourPicks.MaxChoices; choice++)
        {
            if (!colours.Colours(choice, carried) || colours.Swatches(choice).Count == 0)
            {
                continue;
            }

            CharacterPartSlot owner = OnlySlotCarrying(slots, DyeMask(colours, choice));
            if (owner == null)
            {
                body.Add(choice);
            }
            else
            {
                if (!bySlot.TryGetValue(owner, out List<int> list))
                {
                    list = new List<int>();
                    bySlot.Add(owner, list);
                }

                list.Add(choice);
            }
        }

        if (body.Count > 0)
        {
            groups.Add(new CharacterGroup(CharacterGroupKind.Body, BodyLabel, null, body));
        }

        foreach (CharacterPartSlot slot in slots)
        {
            bySlot.TryGetValue(slot, out List<int> choices);
            if (slot.Options.Count > 1 || choices != null)
            {
                groups.Add(new CharacterGroup(CharacterGroupKind.Part, slot.Slot.ToString(), slot, (IReadOnlyList<int>)choices ?? new int[0]));
            }
        }

        return groups;
    }

    /// <summary>
    /// The parts the rows are built over: the first look of <paramref name="looks"/> that wears parts, or null.
    /// </summary>
    public static CharacterParts PartsOf(CharacterLookCatalog looks)
    {
        for (int i = 0; looks != null && i < looks.Count; i++)
        {
            CharacterLook look = looks.Get(i);
            if (look != null && look.Parts != null)
            {
                return look.Parts;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="group"/>'s row shows while <paramref name="look"/> is picked: the Look row always, the
    /// others only for a look wearing the parts they were built over.
    /// </summary>
    public static bool Shows(CharacterGroup group, CharacterLook look, CharacterParts parts)
    {
        return group.Kind == CharacterGroupKind.Look || (look != null && parts != null && look.Parts == parts);
    }

    /// <summary>
    /// Whether colour choice <paramref name="choice"/> of <paramref name="group"/> shows while <paramref name="look"/>
    /// wears <paramref name="worn"/>: on the Body row while the look's sheets or worn parts carry its dyes, on a part's
    /// row while the option worn in its slot carries them. A part not worn hides its colours; their picks stay.
    /// </summary>
    public static bool ShowsChoice(CharacterGroup group, int choice, CharacterColours colours, CharacterLook look, CharacterParts parts, CharacterPartPicks worn)
    {
        if (!Shows(group, look, parts) || group.Kind == CharacterGroupKind.Look)
        {
            return false;
        }

        int mask = DyeMask(colours, choice);
        if (group.Kind == CharacterGroupKind.Body)
        {
            int dyes = (look.AnimationSet != null ? look.AnimationSet.Dyes : 0) | parts.DyesOf(worn);
            return (dyes & mask) != 0;
        }

        int option = parts.Shown(worn, group.Slot.Slot);
        return option >= 0 && (group.Slot.Options[option].Dyes & mask) != 0;
    }

    /// <summary>
    /// The dyes colour choice <paramref name="choice"/> colours, a bit per <see cref="CharacterDye"/>.
    /// </summary>
    public static int DyeMask(CharacterColours colours, int choice)
    {
        CharacterColourChoice colourChoice = colours != null ? colours.Get(choice) : null;
        int mask = 0;
        if (colourChoice != null)
        {
            foreach (CharacterDye dye in colourChoice.Dyes)
            {
                mask |= 1 << (int)dye;
            }
        }

        return mask;
    }

    /// <summary>
    /// The one slot of <paramref name="slots"/> one of whose options carries a dye of <paramref name="mask"/>, or null
    /// when none or several do.
    /// </summary>
    private static CharacterPartSlot OnlySlotCarrying(List<CharacterPartSlot> slots, int mask)
    {
        CharacterPartSlot found = null;
        foreach (CharacterPartSlot slot in slots)
        {
            foreach (CharacterPartOption option in slot.Options)
            {
                if ((option.Dyes & mask) == 0)
                {
                    continue;
                }

                if (found != null)
                {
                    return null;
                }

                found = slot;
                break;
            }
        }

        return found;
    }
}
