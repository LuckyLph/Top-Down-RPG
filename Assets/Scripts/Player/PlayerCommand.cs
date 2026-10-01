using UnityEngine;

public readonly struct PlayerCommand
{
    public PlayerCommand(Vector2 move, bool attack)
    {
        Move = move;
        Attack = attack;
    }

    public Vector2 Move { get; }
    public bool Attack { get; }
}
