using UnityEngine;

/// <summary>
/// <see cref="ICharacterPreview"/> that draws nothing and records what the Character screen asked of it.
/// </summary>
public sealed class FakeCharacterPreview : ICharacterPreview
{
    public Texture Texture => null;
    public int Direction { get; private set; } = CharacterPreviewStage.South;
    public CharacterPreviewPose Pose { get; private set; }
    public bool IsShown { get; private set; }
    public PlayerWeapon Weapon { get; private set; }
    public int PreviewedChoice { get; private set; } = -1;
    public string PreviewedSwatch { get; private set; }
    public int PickedColoursShown { get; private set; }

    /// <summary>
    /// Records the weapon and starts facing south, standing.
    /// </summary>
    public void Show(PlayerWeapon weapon)
    {
        IsShown = true;
        Weapon = weapon;
        Direction = CharacterPreviewStage.South;
        Pose = CharacterPreviewPose.Standing;
    }

    /// <summary>
    /// Records that it hid.
    /// </summary>
    public void Hide()
    {
        IsShown = false;
    }

    /// <summary>
    /// Turns as the stage does.
    /// </summary>
    public void Turn(int steps)
    {
        Direction = ((Direction + steps) % CharacterDirections.Count + CharacterDirections.Count) % CharacterDirections.Count;
    }

    /// <summary>
    /// Moves to the next pose as the stage does.
    /// </summary>
    public void NextPose()
    {
        Pose = (CharacterPreviewPose)(((int)Pose + 1) % 3);
    }

    /// <summary>
    /// Records the swatch previewed.
    /// </summary>
    public void PreviewColour(int choice, string swatch)
    {
        PreviewedChoice = choice;
        PreviewedSwatch = swatch;
    }

    /// <summary>
    /// Records that the picked colours show again.
    /// </summary>
    public void ShowPickedColours()
    {
        PreviewedChoice = -1;
        PreviewedSwatch = null;
        PickedColoursShown++;
    }
}
