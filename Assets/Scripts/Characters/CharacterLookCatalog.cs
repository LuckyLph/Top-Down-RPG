using System;
using UnityEngine;

/// <summary>
/// Every look a player can be drawn with (Docs/AsepriteCharacters.md, Phase 2), in a fixed order, the first the default:
/// a name, the animation set it draws, baked or imported from Aseprite, and the locomotion settings its gaits play at. A
/// look is identified on the wire by its index here, as a weapon is by its catalog index, safe since join approval
/// refuses other build versions, and in the local settings by its name, so a removed look's name falls back to the
/// first.
/// </summary>
[CreateAssetMenu(fileName = "CharacterLooks", menuName = "TopDownRPG/Characters/Look Catalog")]
public sealed class CharacterLookCatalog : ScriptableObject
{
    [SerializeField] private CharacterLook[] looks = new CharacterLook[0];

    public int Count => looks != null ? looks.Length : 0;

    /// <summary>
    /// Creates an unsaved catalog of <paramref name="catalogLooks"/>, for tests.
    /// </summary>
    public static CharacterLookCatalog Create(params CharacterLook[] catalogLooks)
    {
        CharacterLookCatalog catalog = CreateInstance<CharacterLookCatalog>();
        catalog.looks = catalogLooks;
        return catalog;
    }

    /// <summary>
    /// The look at <paramref name="index"/>, or null when the index is out of range.
    /// </summary>
    public CharacterLook Get(int index)
    {
        return index >= 0 && index < Count ? looks[index] : null;
    }

    /// <summary>
    /// The index of the first look named <paramref name="lookName"/>, or -1 when there is none or the name is empty.
    /// </summary>
    public int IndexOf(string lookName)
    {
        if (string.IsNullOrEmpty(lookName))
        {
            return -1;
        }

        for (int i = 0; i < Count; i++)
        {
            if (looks[i] != null && looks[i].Name == lookName)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// One look of a <see cref="CharacterLookCatalog"/>: its name, shown in the options and kept in the local settings, the
/// animation set it draws, its locomotion settings, and the parts its character can wear, if any.
/// </summary>
[Serializable]
public sealed class CharacterLook
{
    [SerializeField, Tooltip("The look's name, shown in the options and kept in the local settings.")]
    private string name;

    [SerializeField, Tooltip("The clips it draws, baked or imported from Aseprite; it holds every clip a player plays.")]
    private CharacterAnimationSet animationSet;

    [SerializeField, Tooltip("Its gait speed bands, playback rates and direction margin.")]
    private CharacterLocomotionSettings locomotion;

    [SerializeField, Tooltip("The parts its character can wear (Docs/PlayerCharacter.md, Phase 6), baked with its set; none for a look whose parts are its own.")]
    private CharacterParts parts;

    /// <summary>
    /// Creates a look, for tests.
    /// </summary>
    public CharacterLook(string lookName, CharacterAnimationSet lookSet, CharacterLocomotionSettings lookLocomotion, CharacterParts lookParts = null)
    {
        name = lookName;
        animationSet = lookSet;
        locomotion = lookLocomotion;
        parts = lookParts;
    }

    public string Name => name;
    public CharacterAnimationSet AnimationSet => animationSet;
    public CharacterLocomotionSettings Locomotion => locomotion;
    public CharacterParts Parts => parts;
}
