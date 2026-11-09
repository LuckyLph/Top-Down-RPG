using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every clip a character's recipe bakes (Docs/Animation.md, Runtime), written beside its sheets by the character
/// baker's Generate: per clip the sprites by direction and frame, each frame's duration at natural speed, and for a
/// gait its natural speed, and for an attack its contact frame, and which dyes its sheets' palette maps name
/// (<see cref="Dyes"/>, Docs/PlayerCharacter.md, Phase 5). <see cref="CharacterAnimator"/> plays it; runtime code only
/// reads it.
/// </summary>
public sealed class CharacterAnimationSet : ScriptableObject
{
    [SerializeField] private List<CharacterAnimationClip> clips = new();
    [SerializeField, Tooltip("The dyes its sheets' palette maps name, a bit per CharacterDye: what a player's colours change on it. None for sheets without palette maps.")]
    private int dyes;

    public IReadOnlyList<CharacterAnimationClip> Clips => clips;

    /// <summary>
    /// The dyes the sheets' palette maps name, a bit per <see cref="CharacterDye"/> (1 &lt;&lt; dye): what a player's
    /// colours change on this character. 0 for sheets without palette maps, drawn in their own colours only.
    /// </summary>
    public int Dyes => dyes;

    /// <summary>
    /// Creates an unsaved set holding <paramref name="setClips"/> whose sheets' palette maps name
    /// <paramref name="setDyes"/>, for the baker and tests.
    /// </summary>
    public static CharacterAnimationSet Create(IEnumerable<CharacterAnimationClip> setClips, int setDyes = 0)
    {
        CharacterAnimationSet set = CreateInstance<CharacterAnimationSet>();
        set.clips = new List<CharacterAnimationClip>(setClips);
        set.dyes = setDyes;
        return set;
    }

    /// <summary>
    /// Whether the sheets' palette maps name <paramref name="dye"/>.
    /// </summary>
    public bool Carries(CharacterDye dye)
    {
        return dye != CharacterDye.None && (dyes & (1 << (int)dye)) != 0;
    }

    /// <summary>
    /// The first clip drawn without a weapon that shows <paramref name="gait"/>, or null when there is none.
    /// </summary>
    public CharacterAnimationClip Locomotion(CharacterGait gait)
    {
        for (int i = 0; i < clips.Count; i++)
        {
            CharacterAnimationClip clip = clips[i];
            if (clip != null && clip.Gait == gait && !clip.Armed)
            {
                return clip;
            }
        }

        return null;
    }

    /// <summary>
    /// The first clip named <paramref name="clipName"/>, or null when there is none or the name is empty. Looked up
    /// when a weapon is equipped, never per frame.
    /// </summary>
    public CharacterAnimationClip Find(string clipName)
    {
        if (string.IsNullOrEmpty(clipName))
        {
            return null;
        }

        for (int i = 0; i < clips.Count; i++)
        {
            CharacterAnimationClip clip = clips[i];
            if (clip != null && clip.Name == clipName)
            {
                return clip;
            }
        }

        return null;
    }
}
