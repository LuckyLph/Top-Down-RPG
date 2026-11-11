using UnityEngine;

/// <summary>
/// How the HUD times and marks what it shows (Docs/UiRevamp.md, Decisions): how long a failed press's reason stays on
/// the message line, and the health bar's tick, recent loss and low state. Read by <see cref="AbilityBarPresenter"/>
/// and <see cref="PlayerHudPresenter"/> on this machine only; nothing in gameplay reads it.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/UI/HUD Settings", fileName = "HudSettings")]
public sealed class HudSettings : ScriptableObject
{
    [Header("Messages")]
    [SerializeField, Min(0.1f), Tooltip("Seconds a failed press's reason shows on the message line above the health bar.")]
    private float messageSeconds = 1.2f;

    [Header("Health")]
    [SerializeField, Min(1), Tooltip("HP between two ticks on the health bar: one Weasel hit.")]
    private int tickHealth = 10;
    [SerializeField, Min(0f), Tooltip("Seconds the part of the bar just lost shows pale.")]
    private float recentLossSeconds = 0.4f;
    [SerializeField, Range(0f, 1f), Tooltip("The share of the maximum HP at or under which the bar is low: its frame turns Danger and pulses.")]
    private float lowHealthShare = 0.3f;
    [SerializeField, Min(0.1f), Tooltip("Seconds of one pulse of a low bar's frame.")]
    private float lowPulseSeconds = 1f;
    [SerializeField, Range(0f, 1f), Tooltip("A low bar's frame opacity at the dim end of its pulse; it holds at full with Reduce flashing on.")]
    private float lowPulseFloor = 0.35f;

    public float MessageSeconds => Mathf.Max(0.1f, messageSeconds);
    public int TickHealth => Mathf.Max(1, tickHealth);
    public float RecentLossSeconds => Mathf.Max(0f, recentLossSeconds);
    public float LowHealthShare => Mathf.Clamp01(lowHealthShare);
    public float LowPulseSeconds => Mathf.Max(0.1f, lowPulseSeconds);
    public float LowPulseFloor => Mathf.Clamp01(lowPulseFloor);

    /// <summary>
    /// Creates unsaved settings with these values, for tests. The caller owns them and destroys them.
    /// </summary>
    public static HudSettings Create(
        float messageSeconds = 1.2f,
        int tickHealth = 10,
        float recentLossSeconds = 0.4f,
        float lowHealthShare = 0.3f,
        float lowPulseSeconds = 1f,
        float lowPulseFloor = 0.35f)
    {
        HudSettings settings = CreateInstance<HudSettings>();
        settings.messageSeconds = messageSeconds;
        settings.tickHealth = tickHealth;
        settings.recentLossSeconds = recentLossSeconds;
        settings.lowHealthShare = lowHealthShare;
        settings.lowPulseSeconds = lowPulseSeconds;
        settings.lowPulseFloor = lowPulseFloor;
        settings.hideFlags = HideFlags.HideAndDontSave;
        return settings;
    }
}
