/// <summary>
/// Identifies each state of the mob state machine; <see cref="MobController.ChangeState"/> switches by id.
/// </summary>
public enum MobStateId
{
    Idle = 0,
    Patrol = 1,
    Chase = 2,
    AttackRange = 3,
    Return = 4,
    Search = 5
}
