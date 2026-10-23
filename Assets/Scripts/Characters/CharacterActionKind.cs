/// <summary>
/// What an action of a <see cref="CharacterAnimator"/> shows: an attack on its timeline, or a cast (its gather,
/// release and follow-through). Each system stops only its own kind, so ending an attack never stops a cast that
/// started on the same update.
/// </summary>
public enum CharacterActionKind
{
    Attack,
    Cast
}
