using UnityEngine;

/// <summary>
/// A camera that can be kicked: knocked off its follow position by an offset that eases back to nothing, as visual
/// feedback only (Docs/Animation.md, Phase 6).
/// </summary>
public interface ICameraKick
{
    /// <summary>
    /// Knocks the camera <paramref name="offset"/> world units off where it follows, easing back over
    /// <paramref name="seconds"/>; ignored while a larger kick is still under way.
    /// </summary>
    void Kick(Vector2 offset, float seconds);
}
