using UnityEngine;

/// <summary>
/// The Character screen's pose of the character in its preview (Docs/UiRevamp.md, Phase 7).
/// </summary>
public enum CharacterPreviewPose
{
    Standing,
    Running,
    Attacking,
}

/// <summary>
/// The character the Character screen shows (Docs/UiRevamp.md, Phase 7): the local player's look, parts and colours as
/// the local settings pick them, drawn into <see cref="Texture"/> while shown, turned through the 8 directions and posed.
/// </summary>
public interface ICharacterPreview
{
    /// <summary>
    /// The texture the character is drawn into, one texel an art pixel; null while never shown.
    /// </summary>
    Texture Texture { get; }

    /// <summary>
    /// The direction the character faces, a <see cref="CharacterDirections"/> index.
    /// </summary>
    int Direction { get; }

    CharacterPreviewPose Pose { get; }

    /// <summary>
    /// Starts drawing the character facing south and standing, following the local settings, with the gear and attacks
    /// of <paramref name="weapon"/> (null for the starting weapon's).
    /// </summary>
    void Show(PlayerWeapon weapon);

    /// <summary>
    /// Stops drawing the character and following the local settings.
    /// </summary>
    void Hide();

    /// <summary>
    /// Turns the character <paramref name="steps"/> directions: toward the screen's left for a negative count (from
    /// south, south-west), toward its right for a positive one.
    /// </summary>
    void Turn(int steps);

    /// <summary>
    /// Shows the next pose: standing, running, attacking, then standing again.
    /// </summary>
    void NextPose();

    /// <summary>
    /// Shows the local settings' colours with colour choice <paramref name="choice"/> in the swatch named
    /// <paramref name="swatch"/>, before it is picked (the swatch picker's focus).
    /// </summary>
    void PreviewColour(int choice, string swatch);

    /// <summary>
    /// Shows the local settings' colours again, ending a <see cref="PreviewColour"/>.
    /// </summary>
    void ShowPickedColours();
}
