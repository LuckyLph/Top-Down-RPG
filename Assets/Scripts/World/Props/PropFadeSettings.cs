using UnityEngine;

/// <summary>
/// How tall props fade while they hide the local player (Docs/PropGeneration.md, In play): the opacity they fade to,
/// how long a fade takes, the box the player's body covers on screen above its feet, and how much of that box a prop
/// must cover to fade. Read by <see cref="PropFading"/>; visual only, each machine fading for its own player.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Prop Fade Settings", fileName = "PropFadeSettings")]
public sealed class PropFadeSettings : ScriptableObject
{
    [SerializeField, Range(0f, 1f), Tooltip("The opacity a prop fades to while it hides the local player.")]
    private float fadedAlpha = 0.45f;
    [SerializeField, Min(0.01f), Tooltip("How long a prop takes to fade all the way out or back, in seconds.")]
    private float fadeSeconds = 0.15f;
    [SerializeField, Tooltip("The box the local player's body covers on screen above its feet, centred on them: its width and height, in units.")]
    private Vector2 body = new(0.5f, 1.3f);
    [SerializeField, Range(0f, 1f), Tooltip("How much of the body's box a prop's drawing must cover for it to fade.")]
    private float coverShare = 0.25f;

    public float FadedAlpha => fadedAlpha;
    public float FadeSeconds => fadeSeconds;
    public Vector2 Body => body;
    public float CoverShare => coverShare;

    /// <summary>
    /// Creates settings fading to <paramref name="alpha"/> over <paramref name="seconds"/> for a body of
    /// <paramref name="bodySize"/>, a prop covering <paramref name="share"/> of it, for tests.
    /// </summary>
    public static PropFadeSettings Create(float alpha, float seconds, Vector2 bodySize, float share)
    {
        PropFadeSettings settings = CreateInstance<PropFadeSettings>();
        settings.fadedAlpha = alpha;
        settings.fadeSeconds = seconds;
        settings.body = bodySize;
        settings.coverShare = share;
        return settings;
    }
}
