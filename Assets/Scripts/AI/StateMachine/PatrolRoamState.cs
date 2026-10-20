using UnityEngine;

/// <summary>
/// Mob state that walks to a random reachable point near its spawn (inside its home chunk, for a mob of a world), then
/// idles; chases a detected target it can reach.
/// </summary>
public class PatrolRoamState : MobStateBase
{
    /// <summary>
    /// Creates the patrol state for the mob run by <paramref name="brain"/>.
    /// </summary>
    public PatrolRoamState(MobController brain) : base(brain) { }

    public override MobStateId StateId => MobStateId.Patrol;

    /// <summary>
    /// Picks a reachable roam destination and paths to it; goes back to Idle when there is no destination or path.
    /// </summary>
    public override void Enter()
    {
        if (!Patrol.TryGetRoamDestination(PathAgent.CanReachWorldTarget, out Vector2 destination))
        {
            Brain.ChangeState(MobStateId.Idle);
            return;
        }

        bool pathFound = PathAgent.BuildPathToWorld(destination, allowPartial: true);
        if (!pathFound)
        {
            Brain.ChangeState(MobStateId.Idle);
        }
    }

    /// <summary>
    /// Goes to Chase when it spots a target within its leash that it can reach, and to Idle once the destination is
    /// reached or the mob has stalled for <see cref="MobConfig.StuckTimeout"/>.
    /// </summary>
    public override void Tick()
    {
        if (SpotsTarget())
        {
            Brain.ChangeState(MobStateId.Chase);
            return;
        }

        if (PathAgent.ReachedDestination || PathAgent.StalledTime >= Config.StuckTimeout)
        {
            Brain.ChangeState(MobStateId.Idle);
        }
    }

    /// <summary>
    /// Follows the path.
    /// </summary>
    public override void FixedTick()
    {
        PathAgent.FixedTick();
    }
}
