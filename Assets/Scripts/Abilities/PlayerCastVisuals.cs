using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays the player's casts on its character visuals (Docs/Animation.md, Phase 5), on every machine: on the owner from
/// <see cref="PlayerAbilities.CastStarted"/> and <see cref="PlayerAbilities.CastEnded"/>, on a copy from the owner's
/// cast messages (<see cref="PlayerAbilities.RemoteCastStarted"/> and <see cref="PlayerAbilities.RemoteCastCancelled"/>).
/// A cast plays its ability's clip on the ability's cast timeline from its start, toward its aim, or turning with the
/// facing for an ability aimed nowhere, with its clip drawn over the run shown while the caster moves. On the owner the
/// gather waits for the cast to complete, so the release frame shows on exactly the update its orders complete it; a
/// copy plays on its own clock from the cast message. A cancelled cast stops its clip at once; a completed one plays
/// its release and follow-through on. Visual only: nothing in gameplay reads it. Clips are looked up by name in the
/// character's set on an ability's first cast and kept until the character draws another set (its look changed); a
/// set without an ability's clip over the run plays that cast standing.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerAbilities))]
public class PlayerCastVisuals : MonoBehaviour
{
    [SerializeField, Tooltip("The abilities whose casts play.")]
    private PlayerAbilities abilities;

    [SerializeField, Tooltip("The character's visuals, which play the casts.")]
    private CharacterVisuals visuals;

    private readonly Dictionary<AbilityDefinition, CastClips> clips = new();
    private CharacterAnimationSet clipsFrom;

    /// <summary>
    /// Finds the abilities and the visuals when unassigned, and fails loudly without visuals.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
        if (visuals == null)
        {
            Debug.LogError($"{name} has no {nameof(CharacterVisuals)} to play its casts on.", this);
        }
    }

    /// <summary>
    /// Fills empty references from this object and its children.
    /// </summary>
    private void OnValidate()
    {
        ResolveReferences();
    }

    /// <summary>
    /// Follows the abilities' casts, the owner's and a copy's.
    /// </summary>
    private void OnEnable()
    {
        ResolveReferences();
        if (abilities == null)
        {
            return;
        }

        abilities.CastStarted += PlayOwn;
        abilities.CastEnded += HandleCastEnded;
        abilities.RemoteCastStarted += PlayCopy;
        abilities.RemoteCastCancelled += Stop;
    }

    /// <summary>
    /// Stops following the casts and stops any cast showing (death, despawn).
    /// </summary>
    private void OnDisable()
    {
        if (abilities != null)
        {
            abilities.CastStarted -= PlayOwn;
            abilities.CastEnded -= HandleCastEnded;
            abilities.RemoteCastStarted -= PlayCopy;
            abilities.RemoteCastCancelled -= Stop;
        }

        Stop();
    }

    /// <summary>
    /// Plays the owner's <paramref name="cast"/>, its release waiting for the cast to complete.
    /// </summary>
    private void PlayOwn(AbilityCast cast)
    {
        Play(cast, true);
    }

    /// <summary>
    /// Plays another machine's <paramref name="cast"/> on this copy, on its own clock.
    /// </summary>
    private void PlayCopy(AbilityCast cast)
    {
        Play(cast, false);
    }

    /// <summary>
    /// Plays <paramref name="cast"/> from now: its ability's clips on its cast timeline, toward its aim unless the
    /// ability is aimed nowhere, in which case it turns with the facing; with <paramref name="untilRelease"/> the
    /// release waits for the cast to complete. Nothing plays for an ability without a clip.
    /// </summary>
    private void Play(AbilityCast cast, bool untilRelease)
    {
        AbilityDefinition ability = cast.Ability;
        if (visuals == null || ability == null)
        {
            return;
        }

        CastClips found = ClipsFor(ability);
        if (found.Standing == null)
        {
            return;
        }

        bool aimed = ability.Targeting != AbilityTargeting.None;
        Vector2 direction = aimed ? cast.Aim.Direction : Vector2.zero;
        visuals.PlayCast(found.Standing, found.Moving, direction, !aimed, ability.CastTimeline, ability.ChannelBeat, untilRelease);
    }

    /// <summary>
    /// Shows a completed cast's release from now, or stops the clip of a cancelled one.
    /// </summary>
    private void HandleCastEnded(int slot, bool completed)
    {
        if (!completed)
        {
            Stop();
        }
        else if (visuals != null)
        {
            visuals.ReachContact();
        }
    }

    /// <summary>
    /// Stops any cast showing, at once.
    /// </summary>
    private void Stop()
    {
        if (visuals != null)
        {
            visuals.StopAction(CharacterActionKind.Cast);
        }
    }

    /// <summary>
    /// The clips <paramref name="ability"/> names, looked up in the character's set the first time and kept until the
    /// visuals draw another set (the player's look changed); logs an error when the set lacks the standing clip. A set
    /// without the clip drawn over the run (a character drawn by hand) plays the cast standing.
    /// </summary>
    private CastClips ClipsFor(AbilityDefinition ability)
    {
        if (!ReferenceEquals(visuals.AnimationSet, clipsFrom))
        {
            clipsFrom = visuals.AnimationSet;
            clips.Clear();
        }

        if (clips.TryGetValue(ability, out CastClips found))
        {
            return found;
        }

        found = new CastClips(FindClip(ability, ability.CastClip, true), FindClip(ability, ability.MovingCastClip, false));
        clips[ability] = found;
        return found;
    }

    /// <summary>
    /// The clip named <paramref name="clipName"/> in the character's set, or null for an empty name; logs an error when
    /// the set has no such clip and it is <paramref name="required"/>.
    /// </summary>
    private CharacterAnimationClip FindClip(AbilityDefinition ability, string clipName, bool required)
    {
        if (string.IsNullOrEmpty(clipName) || visuals.AnimationSet == null)
        {
            return null;
        }

        CharacterAnimationClip clip = visuals.AnimationSet.Find(clipName);
        if (clip == null && required)
        {
            Debug.LogError($"{name}'s {nameof(CharacterAnimationSet)} has no clip '{clipName}' for {ability.DisplayName}.", this);
        }

        return clip;
    }

    /// <summary>
    /// Test setup without the engine lifecycle: does what <c>OnEnable</c> does, after what <c>OnDisable</c> does.
    /// </summary>
    internal void Configure()
    {
        OnDisable();
        OnEnable();
    }

    /// <summary>
    /// Falls back to the <see cref="PlayerAbilities"/> on this object and the <see cref="CharacterVisuals"/> under it.
    /// </summary>
    private void ResolveReferences()
    {
        if (abilities == null)
        {
            abilities = GetComponent<PlayerAbilities>();
        }

        if (visuals == null)
        {
            visuals = GetComponentInChildren<CharacterVisuals>(true);
        }
    }

    /// <summary>
    /// An ability's clips: standing, and drawn over the run (null for none).
    /// </summary>
    private readonly struct CastClips
    {
        /// <summary>
        /// Creates the pair of <paramref name="standing"/> and <paramref name="moving"/>.
        /// </summary>
        public CastClips(CharacterAnimationClip standing, CharacterAnimationClip moving)
        {
            Standing = standing;
            Moving = moving;
        }

        public CharacterAnimationClip Standing { get; }
        public CharacterAnimationClip Moving { get; }
    }
}
