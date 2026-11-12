using System;
using VContainer.Unity;

/// <summary>
/// The vitals strip of the Character screen in game (Docs/UiRevamp.md, Phase 7, Full screens in game): while the screen
/// is open online, where the game runs on behind it, the strip shows the session's line and the local player's health,
/// and a hit on the local player puts "You're taking damage." on the screen's foot line for
/// <see cref="HudSettings.NoticeSeconds"/> of real time, so the player is told in words, never by flashing. Offline the
/// game is paused behind it, and nothing shows. Gameplay scope; each machine shows its own player's.
/// </summary>
public sealed class VitalsStripPresenter : IStartable, ITickable, IDisposable
{
    public const string SessionLine = "Online · not paused";
    public const string DamageWarning = "You're taking damage.";

    private readonly CharacterScreenView view;
    private readonly CharacterScreenController screen;
    private readonly ISessionStatus session;
    private readonly LocalPlayerTracker localPlayer;
    private readonly HudSettings settings;
    private readonly IWallClock wallClock;
    private Health health;
    private bool shown;
    private bool warning;
    private DateTime warningUntil;

    /// <summary>
    /// Creates the presenter of <paramref name="view"/>'s strip, shown while <paramref name="screen"/> is open and
    /// <paramref name="session"/> is active, over <paramref name="localPlayer"/>'s health.
    /// </summary>
    public VitalsStripPresenter(CharacterScreenView view, CharacterScreenController screen, ISessionStatus session, LocalPlayerTracker localPlayer, HudSettings settings, IWallClock wallClock)
    {
        this.view = view;
        this.screen = screen;
        this.session = session;
        this.localPlayer = localPlayer;
        this.settings = settings;
        this.wallClock = wallClock;
    }

    public bool IsShown => shown;

    /// <summary>
    /// Hides the strip and follows the local player's health.
    /// </summary>
    public void Start()
    {
        view.Vitals.SetShown(false);
        view.Vitals.SetSession(SessionLine);
        localPlayer.Changed += Track;
        Track(localPlayer.Current);
    }

    /// <summary>
    /// Stops following the local player.
    /// </summary>
    public void Dispose()
    {
        localPlayer.Changed -= Track;
        Track(null);
    }

    /// <summary>
    /// Shows the strip while the screen is open online, with the local player's health, and clears the warning once its
    /// time is up or the strip hides.
    /// </summary>
    public void Tick()
    {
        bool online = screen.IsOpen && session.IsActive;
        if (online != shown)
        {
            shown = online;
            view.Vitals.SetShown(online);
        }

        if (online && health != null)
        {
            view.Vitals.SetHealth(health.CurrentHealth, health.MaxHealth);
        }

        if (warning && (!online || wallClock.UtcNow >= warningUntil))
        {
            warning = false;
            view.SetWarning(null);
        }
    }

    /// <summary>
    /// Follows <paramref name="player"/>'s health in place of the last one's.
    /// </summary>
    private void Track(LocalPlayer player)
    {
        if (health != null)
        {
            health.Damaged -= HandleDamaged;
        }

        health = player?.Handle?.Health;
        if (health != null)
        {
            health.Damaged += HandleDamaged;
        }
    }

    /// <summary>
    /// A hit on the local player while the strip shows: says so on the foot line.
    /// </summary>
    private void HandleDamaged(Health.DamageEvent damage)
    {
        if (!shown)
        {
            return;
        }

        warning = true;
        warningUntil = wallClock.UtcNow + TimeSpan.FromSeconds(settings.NoticeSeconds);
        view.SetWarning(DamageWarning);
    }
}
