public interface IMobState
{
    MobStateId StateId { get; }

    void Enter();
    void Tick();
    void FixedTick();
    void Exit();
}
