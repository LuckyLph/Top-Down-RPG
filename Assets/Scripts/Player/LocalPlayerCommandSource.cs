public sealed class LocalPlayerCommandSource : IPlayerCommandSource
{
    private readonly IPlayerInput input;

    public LocalPlayerCommandSource(IPlayerInput input)
    {
        this.input = input;
    }

    public PlayerCommand ReadCommand()
    {
        return new PlayerCommand(input.Move, input.AttackPressedThisFrame);
    }
}
