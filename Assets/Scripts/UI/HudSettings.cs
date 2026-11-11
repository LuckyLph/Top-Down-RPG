using UnityEngine;

/// <summary>
/// How the HUD times what it shows (Docs/UiRevamp.md, Decisions): how long a failed press's reason stays on the
/// ability bar's message line. Read by <see cref="AbilityBarPresenter"/> on this machine only; nothing in gameplay
/// reads it.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/UI/HUD Settings", fileName = "HudSettings")]
public sealed class HudSettings : ScriptableObject
{
    [Header("Messages")]
    [SerializeField, Min(0.1f), Tooltip("Seconds a failed press's reason shows on the message line above the ability bar.")]
    private float messageSeconds = 1.2f;

    public float MessageSeconds => Mathf.Max(0.1f, messageSeconds);

    /// <summary>
    /// Creates unsaved settings showing a message for <paramref name="messageSeconds"/>, for tests. The caller owns
    /// them and destroys them.
    /// </summary>
    public static HudSettings Create(float messageSeconds = 1.2f)
    {
        HudSettings settings = CreateInstance<HudSettings>();
        settings.messageSeconds = messageSeconds;
        settings.hideFlags = HideFlags.HideAndDontSave;
        return settings;
    }
}
