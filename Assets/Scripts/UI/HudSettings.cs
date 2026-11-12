using UnityEngine;

/// <summary>
/// How the HUD times and marks what it shows (Docs/UiRevamp.md, Decisions): how long a failed press's reason stays on
/// the message line and a notice on the notice line, the health bar's tick, recent loss and low state, and how long a
/// chat line stays and where the chat window stands and how large it may be (Phase 12). Read by
/// <see cref="AbilityBarPresenter"/>, <see cref="NoticePresenter"/>, <see cref="PlayerHudPresenter"/> and
/// <see cref="ChatPresenter"/> on this machine only; nothing in gameplay reads it.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/UI/HUD Settings", fileName = "HudSettings")]
public sealed class HudSettings : ScriptableObject
{
    [Header("Messages")]
    [SerializeField, Min(0.1f), Tooltip("Seconds a failed press's reason shows on the message line above the health bar.")]
    private float messageSeconds = 1.2f;
    [SerializeField, Min(0.1f), Tooltip("Seconds of game time a notice shows on the notice line at the top centre before the next one; held while the screen fades.")]
    private float noticeSeconds = 3f;

    [Header("Health")]
    [SerializeField, Min(0), Tooltip("HP between two ticks on the health bar (one Weasel hit is 10); 0 draws none, as the command bar does since Docs/UiRevamp.md's Phase 3.")]
    private int tickHealth = 10;
    [SerializeField, Min(0f), Tooltip("Seconds the part of the bar just lost shows pale.")]
    private float recentLossSeconds = 0.4f;
    [SerializeField, Range(0f, 1f), Tooltip("The share of the maximum HP at or under which the bar is low: its frame turns Danger and pulses.")]
    private float lowHealthShare = 0.3f;
    [SerializeField, Min(0.1f), Tooltip("Seconds of one pulse of a low bar's frame.")]
    private float lowPulseSeconds = 1f;
    [SerializeField, Range(0f, 1f), Tooltip("A low bar's frame opacity at the dim end of its pulse; it holds at full with Reduce flashing on.")]
    private float lowPulseFloor = 0.35f;

    [Header("Chat")]
    [SerializeField, Min(0.1f), Tooltip("Seconds of game time a chat line shows at full while the field is closed, before it fades.")]
    private float chatLineSeconds = 10f;
    [SerializeField, Min(0f), Tooltip("Seconds a chat line takes to fade out once its time is up.")]
    private float chatFadeSeconds = 1f;
    [SerializeField, Tooltip("The chat window's default place, in UI px from the canvas's left and top edges: under the party frames.")]
    private Vector2Int chatOffset = new(8, 98);
    [SerializeField, Tooltip("The chat window's default size in UI px: as wide as the in-game menu's column, down to 8 above the command bar's region at 480 x 270.")]
    private Vector2Int chatSize = new(144, 96);
    [SerializeField, Tooltip("The smallest the player can make the chat window, in UI px: two lines and the field.")]
    private Vector2Int chatMinSize = new(104, 52);
    [SerializeField, Tooltip("The largest the player can make the chat window, in UI px.")]
    private Vector2Int chatMaxSize = new(240, 160);

    public float MessageSeconds => Mathf.Max(0.1f, messageSeconds);
    public float NoticeSeconds => Mathf.Max(0.1f, noticeSeconds);
    public int TickHealth => Mathf.Max(0, tickHealth);
    public float RecentLossSeconds => Mathf.Max(0f, recentLossSeconds);
    public float LowHealthShare => Mathf.Clamp01(lowHealthShare);
    public float LowPulseSeconds => Mathf.Max(0.1f, lowPulseSeconds);
    public float LowPulseFloor => Mathf.Clamp01(lowPulseFloor);
    public float ChatLineSeconds => Mathf.Max(0.1f, chatLineSeconds);
    public float ChatFadeSeconds => Mathf.Max(0f, chatFadeSeconds);
    public Vector2Int ChatOffset => Vector2Int.Max(chatOffset, Vector2Int.zero);
    public Vector2Int ChatMinSize => Vector2Int.Max(chatMinSize, Vector2Int.one);
    public Vector2Int ChatMaxSize => Vector2Int.Max(chatMaxSize, ChatMinSize);
    public Vector2Int ChatSize => Vector2Int.Min(Vector2Int.Max(chatSize, ChatMinSize), ChatMaxSize);

    /// <summary>
    /// Creates unsaved settings with these values, for tests. The caller owns them and destroys them.
    /// </summary>
    public static HudSettings Create(
        float messageSeconds = 1.2f,
        int tickHealth = 10,
        float recentLossSeconds = 0.4f,
        float lowHealthShare = 0.3f,
        float lowPulseSeconds = 1f,
        float lowPulseFloor = 0.35f,
        float noticeSeconds = 3f,
        float chatLineSeconds = 10f,
        float chatFadeSeconds = 1f)
    {
        HudSettings settings = CreateInstance<HudSettings>();
        settings.messageSeconds = messageSeconds;
        settings.tickHealth = tickHealth;
        settings.recentLossSeconds = recentLossSeconds;
        settings.lowHealthShare = lowHealthShare;
        settings.lowPulseSeconds = lowPulseSeconds;
        settings.lowPulseFloor = lowPulseFloor;
        settings.noticeSeconds = noticeSeconds;
        settings.chatLineSeconds = chatLineSeconds;
        settings.chatFadeSeconds = chatFadeSeconds;
        settings.hideFlags = HideFlags.HideAndDontSave;
        return settings;
    }
}
