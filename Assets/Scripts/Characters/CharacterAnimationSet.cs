using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every clip a character's recipe bakes (Docs/Animation.md, Runtime), written beside its sheets by the character
/// baker's Generate: per clip the sprites by direction and frame, each frame's duration at natural speed, and for a
/// gait its natural speed. <see cref="CharacterAnimator"/> plays it; runtime code only reads it.
/// </summary>
public sealed class CharacterAnimationSet : ScriptableObject
{
    [SerializeField] private List<CharacterAnimationClip> clips = new();

    public IReadOnlyList<CharacterAnimationClip> Clips => clips;

    /// <summary>
    /// Creates an unsaved set holding <paramref name="setClips"/>, for the baker and tests.
    /// </summary>
    public static CharacterAnimationSet Create(IEnumerable<CharacterAnimationClip> setClips)
    {
        CharacterAnimationSet set = CreateInstance<CharacterAnimationSet>();
        set.clips = new List<CharacterAnimationClip>(setClips);
        return set;
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
}
