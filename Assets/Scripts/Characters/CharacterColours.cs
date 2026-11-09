using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The colours a player can give its character (Docs/PlayerCharacter.md, Phase 5), edited in the editor: swatch lists
/// (skin tones, hair, eyes, cloth, leather, metal), each a list of named swatches, and the choices a player makes, in
/// a fixed order (a choice is named on the wire by its index here, safe since join approval refuses other builds; the
/// local settings name its swatch). A choice picks a swatch of one list for the dyes it colours, the swatch's ramps
/// going to its dyes in order, and names its own swatch, the colours the look was baked in, which it keeps until the
/// player picks another. <see cref="WritePalette"/> turns a player's <see cref="CharacterColourPicks"/> into the
/// palette the lit sprite shader reads (<see cref="CharacterPaletteMap"/>). Runtime code only reads it.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Characters/Character Colours", fileName = "CharacterColours")]
public sealed class CharacterColours : ScriptableObject
{
    [SerializeField, Tooltip("The swatch lists the choices pick from: each a name and its swatches, a ramp for each dye of the choices using it.")]
    private List<CharacterSwatchList> lists = new();

    [SerializeField, Tooltip("What a player chooses, in a fixed order (named on the wire by index), each a row of the options menu: the list it picks from, its own swatch (the look's baked colours) and the dyes the swatch's ramps colour, in order. Every dye a look's palette maps carry needs a choice.")]
    private List<CharacterColourChoice> choices = new();

    private static readonly CharacterSwatch[] NoSwatches = new CharacterSwatch[0];

    /// <summary>
    /// How many choices a player makes.
    /// </summary>
    public int Count => choices.Count;

    /// <summary>
    /// Creates an unsaved set of colours, for tests.
    /// </summary>
    public static CharacterColours Create(IEnumerable<CharacterSwatchList> swatchLists, IEnumerable<CharacterColourChoice> colourChoices)
    {
        CharacterColours colours = CreateInstance<CharacterColours>();
        colours.lists = new List<CharacterSwatchList>(swatchLists);
        colours.choices = new List<CharacterColourChoice>(colourChoices);
        return colours;
    }

    /// <summary>
    /// The choice at <paramref name="index"/>, or null outside the list.
    /// </summary>
    public CharacterColourChoice Get(int index)
    {
        return index >= 0 && index < choices.Count ? choices[index] : null;
    }

    /// <summary>
    /// The index of the choice named <paramref name="choiceName"/>, or -1.
    /// </summary>
    public int IndexOf(string choiceName)
    {
        for (int i = 0; i < choices.Count; i++)
        {
            if (choices[i] != null && choices[i].Name == choiceName)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The swatches choice <paramref name="choice"/> picks from, its list's; none for a choice or list that does not
    /// exist.
    /// </summary>
    public IReadOnlyList<CharacterSwatch> Swatches(int choice)
    {
        CharacterColourChoice colourChoice = Get(choice);
        if (colourChoice == null)
        {
            return NoSwatches;
        }

        foreach (CharacterSwatchList list in lists)
        {
            if (list != null && list.Name == colourChoice.List)
            {
                return list.Swatches;
            }
        }

        return NoSwatches;
    }

    /// <summary>
    /// The index of choice <paramref name="choice"/>'s own swatch in its list, or -1 when its list lacks it.
    /// </summary>
    public int OwnIndex(int choice)
    {
        CharacterColourChoice colourChoice = Get(choice);
        return colourChoice != null ? IndexOfSwatch(choice, colourChoice.Own) : -1;
    }

    /// <summary>
    /// The index of the swatch named <paramref name="swatchName"/> in choice <paramref name="choice"/>'s list, or -1.
    /// </summary>
    public int IndexOfSwatch(int choice, string swatchName)
    {
        if (string.IsNullOrEmpty(swatchName))
        {
            return -1;
        }

        IReadOnlyList<CharacterSwatch> swatches = Swatches(choice);
        for (int i = 0; i < swatches.Count; i++)
        {
            if (swatches[i] != null && swatches[i].Name == swatchName)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// <paramref name="picks"/> with the swatch named <paramref name="swatchName"/> picked for choice
    /// <paramref name="choice"/>: none (its own) for an empty or unknown name or its own swatch's, so picks that show the
    /// look's own colours are always the default.
    /// </summary>
    public CharacterColourPicks Pick(CharacterColourPicks picks, int choice, string swatchName)
    {
        int swatch = IndexOfSwatch(choice, swatchName);
        return picks.With(choice, swatch == OwnIndex(choice) ? -1 : swatch);
    }

    /// <summary>
    /// The swatch <paramref name="picks"/> shows for choice <paramref name="choice"/>: the one picked, else its own;
    /// null when neither is in its list.
    /// </summary>
    public CharacterSwatch Shown(CharacterColourPicks picks, int choice)
    {
        IReadOnlyList<CharacterSwatch> swatches = Swatches(choice);
        int picked = picks.Get(choice);
        int index = picked >= 0 && picked < swatches.Count ? picked : OwnIndex(choice);
        return index >= 0 && index < swatches.Count ? swatches[index] : null;
    }

    /// <summary>
    /// Whether choice <paramref name="choice"/> colours any dye in <paramref name="dyes"/>, a mask with a bit per
    /// <see cref="CharacterDye"/> (a set's <see cref="CharacterAnimationSet.Dyes"/>).
    /// </summary>
    public bool Colours(int choice, int dyes)
    {
        CharacterColourChoice colourChoice = Get(choice);
        if (colourChoice == null)
        {
            return false;
        }

        foreach (CharacterDye dye in colourChoice.Dyes)
        {
            if ((dyes & (1 << (int)dye)) != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Writes the palette of <paramref name="picks"/> into <paramref name="pixels"/>
    /// (<see cref="CharacterPaletteMap.PaletteSize"/> of them, a row of tones per dye): each choice's dyes in the ramps
    /// of the swatch it shows, in order, opaque; a dye no choice colours stays clear, where the shader keeps the sheet's
    /// own colour.
    /// </summary>
    public void WritePalette(CharacterColourPicks picks, Color32[] pixels)
    {
        Array.Clear(pixels, 0, pixels.Length);
        for (int c = 0; c < choices.Count; c++)
        {
            CharacterColourChoice choice = choices[c];
            CharacterSwatch swatch = Shown(picks, c);
            if (choice == null || swatch == null)
            {
                continue;
            }

            for (int d = 0; d < choice.Dyes.Count && d < swatch.Ramps.Count; d++)
            {
                int row = (int)choice.Dyes[d];
                if (row <= 0 || row >= CharacterPaletteMap.Rows)
                {
                    continue;
                }

                ColourRamp ramp = swatch.Ramps[d];
                for (int tone = 0; tone < ColourRamp.ToneCount; tone++)
                {
                    Color32 colour = ramp.Tone(tone);
                    colour.a = 255;
                    pixels[CharacterPaletteMap.Code((CharacterDye)row, tone)] = colour;
                }
            }
        }
    }

    /// <summary>
    /// Warns about choices past what the wire carries, a choice whose list or own swatch is missing, a swatch with fewer
    /// ramps than its choice's dyes, and a dye two choices colour.
    /// </summary>
    private void OnValidate()
    {
        if (choices.Count > CharacterColourPicks.MaxChoices)
        {
            Debug.LogWarning($"{name}: {choices.Count} choices, but a player's picks carry {CharacterColourPicks.MaxChoices}.", this);
        }

        int coloured = 0;
        for (int c = 0; c < choices.Count; c++)
        {
            CharacterColourChoice choice = choices[c];
            if (choice == null)
            {
                continue;
            }

            if (OwnIndex(c) < 0)
            {
                Debug.LogWarning($"{name}: choice {choice.Name} has no list {choice.List} holding its own swatch {choice.Own}.", this);
            }

            foreach (CharacterSwatch swatch in Swatches(c))
            {
                if (swatch != null && swatch.Ramps.Count < choice.Dyes.Count)
                {
                    Debug.LogWarning($"{name}: swatch {swatch.Name} has {swatch.Ramps.Count} ramps, but choice {choice.Name} colours {choice.Dyes.Count} dyes.", this);
                }
            }

            foreach (CharacterDye dye in choice.Dyes)
            {
                int bit = 1 << (int)dye;
                if ((coloured & bit) != 0)
                {
                    Debug.LogWarning($"{name}: dye {dye} is coloured by two choices.", this);
                }

                coloured |= bit;
            }
        }
    }
}

/// <summary>
/// One swatch of a <see cref="CharacterSwatchList"/>: its name, which the options menu shows and the local settings
/// keep, and a ramp for each dye of the choices that pick it, in their order.
/// </summary>
[Serializable]
public sealed class CharacterSwatch
{
    [SerializeField] private string name;
    [SerializeField] private ColourRamp[] ramps = new ColourRamp[0];

    /// <summary>
    /// Creates an empty swatch, for Unity serialization and the inspector.
    /// </summary>
    public CharacterSwatch()
    {
    }

    /// <summary>
    /// Creates a swatch named <paramref name="swatchName"/> of <paramref name="swatchRamps"/>, for tests and tools.
    /// </summary>
    public CharacterSwatch(string swatchName, params ColourRamp[] swatchRamps)
    {
        name = swatchName;
        ramps = swatchRamps;
    }

    public string Name => name;
    public IReadOnlyList<ColourRamp> Ramps => ramps ?? new ColourRamp[0];
}

/// <summary>
/// A named list of swatches the <see cref="CharacterColours"/>' choices pick from: the skin tones, or the cloths a
/// tunic, a shirt and trousers share.
/// </summary>
[Serializable]
public sealed class CharacterSwatchList
{
    [SerializeField] private string name;
    [SerializeField] private List<CharacterSwatch> swatches = new();

    /// <summary>
    /// Creates an empty list, for Unity serialization and the inspector.
    /// </summary>
    public CharacterSwatchList()
    {
    }

    /// <summary>
    /// Creates a list named <paramref name="listName"/> of <paramref name="listSwatches"/>, for tests and tools.
    /// </summary>
    public CharacterSwatchList(string listName, IEnumerable<CharacterSwatch> listSwatches)
    {
        name = listName;
        swatches = new List<CharacterSwatch>(listSwatches);
    }

    public string Name => name;
    public IReadOnlyList<CharacterSwatch> Swatches => swatches;
}

/// <summary>
/// A choice a player makes of the <see cref="CharacterColours"/>: its name, the options menu row's label and the local
/// settings' key; the list it picks from; its own swatch, the colours the look was baked in; and the dyes the swatch's
/// ramps colour, in order.
/// </summary>
[Serializable]
public sealed class CharacterColourChoice
{
    [SerializeField] private string name;
    [SerializeField] private string list;
    [SerializeField] private string own;
    [SerializeField] private CharacterDye[] dyes = new CharacterDye[0];

    /// <summary>
    /// Creates an empty choice, for Unity serialization and the inspector.
    /// </summary>
    public CharacterColourChoice()
    {
    }

    /// <summary>
    /// Creates a choice named <paramref name="choiceName"/> picking from <paramref name="listName"/>, its own swatch
    /// <paramref name="ownSwatch"/>, colouring <paramref name="choiceDyes"/>, for tests and tools.
    /// </summary>
    public CharacterColourChoice(string choiceName, string listName, string ownSwatch, params CharacterDye[] choiceDyes)
    {
        name = choiceName;
        list = listName;
        own = ownSwatch;
        dyes = choiceDyes;
    }

    public string Name => name;
    public string List => list;
    public string Own => own;
    public IReadOnlyList<CharacterDye> Dyes => dyes ?? new CharacterDye[0];
}
