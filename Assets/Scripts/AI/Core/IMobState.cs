/// <summary>
/// One state of a mob's brain (<see cref="MobController"/>). The brain calls <see cref="Enter"/> and
/// <see cref="Exit"/> on transitions, <see cref="Tick"/> every frame and <see cref="FixedTick"/> every physics step
/// while the state is current.
/// </summary>
public interface IMobState
{
    MobStateId StateId { get; }

    /// <summary>
    /// Called when the brain switches to this state.
    /// </summary>
    void Enter();
    /// <summary>
    /// Per-frame decisions such as transitions and repaths, run after perception ticks; skipped while the mob is
    /// stunned.
    /// </summary>
    void Tick();
    /// <summary>
    /// Per-physics-step movement, run before the motor applies its velocity; skipped while the mob is stunned.
    /// </summary>
    void FixedTick();
    /// <summary>
    /// Called when the brain leaves this state, including when the brain is rebuilt.
    /// </summary>
    void Exit();
}
