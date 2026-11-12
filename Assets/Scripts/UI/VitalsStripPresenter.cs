using System;
using System.Collections.Generic;
using VContainer.Unity;

/// <summary>
/// The vitals strip of the full screens opened in game (Docs/UiRevamp.md, Full screens in game; Phase 7 for the
/// Character screen, Phase 8 for the options screen): while a screen is open online, where the game runs on behind it,
/// its strip shows the session's line and the local player's health, and a hit on the local player puts "You're taking
/// damage." on its foot line for <see cref="HudSettings.NoticeSeconds"/> of real time, so the player is told in words,
/// never by flashing. Offline the game is paused behind it, and nothing shows. Gameplay scope; each machine shows its own
/// player's.
/// </summary>
public sealed class VitalsStripPresenter : IStartable, ITickable, IDisposable
{
    public const string SessionLine = "Online · not paused";
    public const string DamageWarning = "You're taking damage.";

    private readonly IReadOnlyList<IVitalsScreen> screens;
    private readonly ISessionStatus session;
    private readonly LocalPlayerTracker localPlayer;
    private readonly HudSettings settings;
    private readonly IWallClock wallClock;
    private readonly bool[] shown;
    private readonly bool[] warned;
    private Health health;
    private DateTime warningUntil;

    /// <summary>
    /// Creates the presenter of <paramref name="screens"/>' strips, each shown while its screen is open and
    /// <paramref name="session"/> is active, over <paramref name="localPlayer"/>'s health.
    /// </summary>
    public VitalsStripPresenter(IReadOnlyList<IVitalsScreen> screens, ISessionStatus session, LocalPlayerTracker localPlayer, HudSettings settings, IWallClock wallClock)
    {
        this.screens = screens;
        this.session = session;
        this.localPlayer = localPlayer;
        this.settings = settings;
        this.wallClock = wallClock;
        shown = new bool[screens.Count];
        warned = new bool[screens.Count];
    }

    /// <summary>
    /// Whether any screen shows its strip.
    /// </summary>
    public bool IsShown
    {
        get
        {
            for (int i = 0; i < shown.Length; i++)
            {
                if (shown[i])
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Hides every strip and follows the local player's health.
    /// </summary>
    public void Start()
    {
        for (int i = 0; i < screens.Count; i++)
        {
            screens[i].Vitals.SetShown(false);
            screens[i].Vitals.SetSession(SessionLine);
        }

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
    /// Shows each strip while its screen is open online, with the local player's health, and clears a warning once its
    /// time is up or its strip hides.
    /// </summary>
    public void Tick()
    {
        bool timeUp = wallClock.UtcNow >= warningUntil;
        for (int i = 0; i < screens.Count; i++)
        {
            IVitalsScreen screen = screens[i];
            bool online = screen.IsOpen && session.IsActive;
            if (online != shown[i])
            {
                shown[i] = online;
                screen.Vitals.SetShown(online);
            }

            if (online && health != null)
            {
                screen.Vitals.SetHealth(health.CurrentHealth, health.MaxHealth);
            }

            if (warned[i] && (!online || timeUp))
            {
                warned[i] = false;
                screen.SetWarning(null);
            }
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
    /// A hit on the local player: says so on the foot line of each screen showing its strip.
    /// </summary>
    private void HandleDamaged(Health.DamageEvent damage)
    {
        for (int i = 0; i < screens.Count; i++)
        {
            if (shown[i])
            {
                warned[i] = true;
                warningUntil = wallClock.UtcNow + TimeSpan.FromSeconds(settings.NoticeSeconds);
                screens[i].SetWarning(DamageWarning);
            }
        }
    }
}
