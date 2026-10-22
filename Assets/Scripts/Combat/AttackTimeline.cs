using UnityEngine;

/// <summary>
/// The phases of an attack (Docs/Animation.md, Attack timeline): none, the windup before the hit, the contact that
/// starts as the hit resolves, and the recovery after it.
/// </summary>
public enum AttackPhase
{
    None,
    Windup,
    Contact,
    Recovery
}

/// <summary>
/// How long each phase of an attack lasts, in seconds: the windup up to contact, when the hit resolves, then the
/// contact and the recovery. The attack state and the animator's action layer both read the phase at a time since the
/// attack's start from it, so the contact frame shows exactly when the hit lands.
/// </summary>
public readonly struct AttackTimeline
{
    /// <summary>
    /// Creates a timeline of the three phases' durations, each at least zero.
    /// </summary>
    public AttackTimeline(float windup, float contact, float recovery)
    {
        Windup = Mathf.Max(0f, windup);
        Contact = Mathf.Max(0f, contact);
        Recovery = Mathf.Max(0f, recovery);
    }

    public float Windup { get; }
    public float Contact { get; }
    public float Recovery { get; }
    public float Duration => Windup + Contact + Recovery;

    /// <summary>
    /// This timeline played at <paramref name="attackSpeed"/>: every phase divided by it. A speed that is not positive
    /// keeps it as it is.
    /// </summary>
    public AttackTimeline At(float attackSpeed)
    {
        return attackSpeed > 0f ? new AttackTimeline(Windup / attackSpeed, Contact / attackSpeed, Recovery / attackSpeed) : this;
    }

    /// <summary>
    /// The phase <paramref name="elapsed"/> seconds after the attack's start: the windup before contact, the contact
    /// and the recovery for their durations after it, and none once the attack is over.
    /// </summary>
    public AttackPhase PhaseAt(float elapsed)
    {
        if (elapsed < Windup)
        {
            return AttackPhase.Windup;
        }

        if (elapsed < Windup + Contact)
        {
            return AttackPhase.Contact;
        }

        return elapsed < Windup + Contact + Recovery ? AttackPhase.Recovery : AttackPhase.None;
    }
}
