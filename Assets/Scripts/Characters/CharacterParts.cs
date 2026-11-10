using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The parts a look's character can wear (Docs/PlayerCharacter.md, Phase 6), written by the character baker beside the
/// look's sheets: the body's layer (everything the parts do not draw, with each clip's frame size, feet and material
/// table, <see cref="CharacterLayer"/>), the inner line gap the baker shades with, and per <see cref="CharacterSlot"/>
/// its options in a fixed order (an option's index goes on the wire, safe since join approval refuses other builds; the
/// local settings name it), the first the look's own. Each option holds its layers: one, or for a slot whose part
/// depends on another's (the hair under each headwear, a weapon's gear under each cape), one per option of that slot;
/// none where it draws nothing. The weapon's slot is not a player's pick: it shows the gear of the weapon equipped
/// (Docs/PlayerCharacter.md, Phase 7). The look's set is the default picks' outfit; the <see cref="CharacterComposer"/> composes any other. Runtime code
/// only reads it.
/// </summary>
public sealed class CharacterParts : ScriptableObject
{
    [SerializeField, Tooltip("The look's set, which the default picks show and whose clips every outfit keeps.")]
    private CharacterAnimationSet look;

    [SerializeField, Tooltip("The body's layer: everything the parts do not draw.")]
    private TextAsset body;

    [SerializeField, Min(0f), Tooltip("The depth gap past which a nearer part's edge is lined, as the baker shades it.")]
    private float innerLineGap;

    [SerializeField, Tooltip("The slots and their options, in CharacterSlot order.")]
    private List<CharacterPartSlot> slots = new();

    public CharacterAnimationSet Look => look;
    public TextAsset Body => body;
    public float InnerLineGap => innerLineGap;
    public IReadOnlyList<CharacterPartSlot> Slots => slots;

    /// <summary>
    /// Every dye an option's layers name, a bit per <see cref="CharacterDye"/>.
    /// </summary>
    public int Dyes
    {
        get
        {
            int dyes = 0;
            foreach (CharacterPartSlot slot in slots)
            {
                foreach (CharacterPartOption option in slot.Options)
                {
                    dyes |= option.Dyes;
                }
            }

            return dyes;
        }
    }

    /// <summary>
    /// Creates unsaved parts, for the baker and tests.
    /// </summary>
    public static CharacterParts Create(CharacterAnimationSet lookSet, TextAsset bodyLayer, float gap, IEnumerable<CharacterPartSlot> partSlots)
    {
        CharacterParts parts = CreateInstance<CharacterParts>();
        parts.look = lookSet;
        parts.body = bodyLayer;
        parts.innerLineGap = gap;
        parts.slots = new List<CharacterPartSlot>(partSlots);
        return parts;
    }

    /// <summary>
    /// The slot <paramref name="slot"/>, or null when the parts have none.
    /// </summary>
    public CharacterPartSlot Get(CharacterSlot slot)
    {
        foreach (CharacterPartSlot partSlot in slots)
        {
            if (partSlot != null && partSlot.Slot == slot)
            {
                return partSlot;
            }
        }

        return null;
    }

    /// <summary>
    /// The option <paramref name="picks"/> shows in <paramref name="slot"/>: the one picked, or the first for a pick the
    /// slot lacks; -1 for a slot without options.
    /// </summary>
    public int Shown(CharacterPartPicks picks, CharacterSlot slot)
    {
        CharacterPartSlot partSlot = Get(slot);
        if (partSlot == null || partSlot.Options.Count == 0)
        {
            return -1;
        }

        int option = picks.Get(slot);
        return option < partSlot.Options.Count ? option : 0;
    }

    /// <summary>
    /// Whether <paramref name="picks"/> show the look's own parts, the first option of every slot, so the look's set
    /// draws them.
    /// </summary>
    public bool IsOwn(CharacterPartPicks picks)
    {
        foreach (CharacterSlot slot in (CharacterSlot[])Enum.GetValues(typeof(CharacterSlot)))
        {
            if (Shown(picks, slot) > 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The layer <paramref name="picks"/> lay in <paramref name="slot"/>: the shown option's, under the shown option of
    /// the slot it depends on; null where it draws nothing.
    /// </summary>
    public TextAsset LayerOf(CharacterPartPicks picks, CharacterSlot slot)
    {
        int option = Shown(picks, slot);
        if (option < 0)
        {
            return null;
        }

        CharacterPartSlot partSlot = Get(slot);
        IReadOnlyList<TextAsset> layers = partSlot.Options[option].Layers;
        int variant = partSlot.VariesWith >= 0 ? Mathf.Max(0, Shown(picks, (CharacterSlot)partSlot.VariesWith)) : 0;
        return variant < layers.Count ? layers[variant] : null;
    }

    /// <summary>
    /// The dyes the options <paramref name="picks"/> show name.
    /// </summary>
    public int DyesOf(CharacterPartPicks picks)
    {
        int dyes = 0;
        foreach (CharacterPartSlot partSlot in slots)
        {
            int option = partSlot != null ? Shown(picks, partSlot.Slot) : -1;
            dyes |= option >= 0 ? partSlot.Options[option].Dyes : 0;
        }

        return dyes;
    }
}

/// <summary>
/// A slot of <see cref="CharacterParts"/>: which <see cref="CharacterSlot"/> it is, the slot its layers depend on (as a
/// <see cref="CharacterSlot"/> index, -1 for none), and its options, the first the look's own.
/// </summary>
[Serializable]
public sealed class CharacterPartSlot
{
    [SerializeField] private CharacterSlot slot;
    [SerializeField] private int variesWith = -1;
    [SerializeField] private List<CharacterPartOption> options = new();

    /// <summary>
    /// Creates an empty slot, for Unity serialization.
    /// </summary>
    public CharacterPartSlot()
    {
    }

    /// <summary>
    /// Creates a slot of <paramref name="slotOptions"/>, its layers depending on <paramref name="dependsOn"/> (-1 for
    /// none), for the baker and tests.
    /// </summary>
    public CharacterPartSlot(CharacterSlot partSlot, int dependsOn, IEnumerable<CharacterPartOption> slotOptions)
    {
        slot = partSlot;
        variesWith = dependsOn;
        options = new List<CharacterPartOption>(slotOptions);
    }

    public CharacterSlot Slot => slot;
    public int VariesWith => variesWith;
    public IReadOnlyList<CharacterPartOption> Options => options;

    /// <summary>
    /// Whether the slot shows the gear of the weapon the player has equipped rather than an option it picks
    /// (Docs/PlayerCharacter.md, Phase 7): no options row, nothing kept in the local settings or sent as a pick.
    /// </summary>
    public bool FollowsWeapon => slot == CharacterSlot.Weapon;

    /// <summary>
    /// The option a weapon naming <paramref name="gear"/> shows: the option of that name, else the first drawing
    /// nothing (every layer null), else the first.
    /// </summary>
    public int GearOption(string gear)
    {
        int named = string.IsNullOrEmpty(gear) ? -1 : IndexOf(gear);
        if (named >= 0)
        {
            return named;
        }

        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] != null && options[i].DrawsNothing)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// The index of the option named <paramref name="optionName"/>, or -1.
    /// </summary>
    public int IndexOf(string optionName)
    {
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] != null && options[i].Name == optionName)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// An option of a <see cref="CharacterPartSlot"/>: its name, shown in the options and kept in the local settings, the
/// dyes its layers name, and its layers (one, or one per option of the slot it depends on; null where it draws nothing).
/// </summary>
[Serializable]
public sealed class CharacterPartOption
{
    [SerializeField] private string name;
    [SerializeField] private int dyes;
    [SerializeField] private TextAsset[] layers = Array.Empty<TextAsset>();

    /// <summary>
    /// Creates an empty option, for Unity serialization.
    /// </summary>
    public CharacterPartOption()
    {
    }

    /// <summary>
    /// Creates an option, for the baker and tests.
    /// </summary>
    public CharacterPartOption(string optionName, int optionDyes, TextAsset[] optionLayers)
    {
        name = optionName;
        dyes = optionDyes;
        layers = optionLayers ?? Array.Empty<TextAsset>();
    }

    public string Name => name;
    public int Dyes => dyes;
    public IReadOnlyList<TextAsset> Layers => layers ?? Array.Empty<TextAsset>();

    /// <summary>
    /// Whether the option draws nothing under any option of the slot it varies with.
    /// </summary>
    public bool DrawsNothing
    {
        get
        {
            foreach (TextAsset layer in Layers)
            {
                if (layer != null)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
