using UnityEngine;

/// <summary>
/// The time of day (Docs/Lighting.md, Phase 3): where in the day and night cycle the world clock lies, the light and the
/// sun there. The time of day is the settings' start time plus the cycles the world clock has played, so it is saved
/// with the game as the world clock is and a loaded save resumes it. Where game state is decided (offline, the host)
/// the world clock is the session's own (<see cref="SessionSave.Time"/>); a client takes the host's from the moment it
/// last heard it (<see cref="Anchor"/>, sent by <see cref="DayClockSync"/>) and counts on from there on the network
/// time, so every machine of a session sees the same sky; before it first hears it, its own. A local offset moves this
/// machine's time of day alone (the dev panel). Cosmetic: nothing in the game's rules reads it.
/// </summary>
public sealed class DayClock
{
    private readonly DayNightSettings settings;
    private readonly SessionSave session;
    private readonly IGameAuthority authority;
    private readonly INetworkClock network;
    private double anchorWorldTime;
    private double anchorServerTime;
    private float localOffset;

    /// <summary>
    /// Creates the clock of <paramref name="daySettings"/> over <paramref name="sessionSave"/>'s world clock, a
    /// client's following the host's on <paramref name="networkClock"/>.
    /// </summary>
    public DayClock(DayNightSettings daySettings, SessionSave sessionSave, IGameAuthority gameAuthority, INetworkClock networkClock)
    {
        settings = daySettings;
        session = sessionSave;
        authority = gameAuthority;
        network = networkClock;
    }

    public DayNightSettings Settings => settings;

    /// <summary>
    /// Whether this client has heard the host's world clock.
    /// </summary>
    public bool IsAnchored { get; private set; }

    /// <summary>
    /// How far this machine's time of day is moved from the session's, as a share of the cycle (0 to follow it).
    /// </summary>
    public float LocalOffset
    {
        get => localOffset;
        set => localOffset = value - Mathf.Floor(value);
    }

    /// <summary>
    /// The world clock the day follows, in seconds: the session's own where game state is decided, else the host's last
    /// heard plus the network time since, else this machine's own.
    /// </summary>
    public double WorldTime => authority.IsAuthoritative || !IsAnchored
        ? session.Time
        : anchorWorldTime + (network.ServerTime - anchorServerTime);

    /// <summary>
    /// The session's time of day, without the local offset, in [0, 1).
    /// </summary>
    public float SessionPhase => settings.PhaseAt(WorldTime);

    /// <summary>
    /// This machine's time of day, the local offset included, in [0, 1): 0 at sunrise, the day's share at sunset.
    /// </summary>
    public float Phase
    {
        get
        {
            float phase = SessionPhase + localOffset;
            phase -= Mathf.Floor(phase);
            return phase >= 1f ? 0f : phase;
        }
    }

    public DayLight Light => settings.LightAt(Phase);
    public SunState Sun => settings.SunAt(Phase);

    /// <summary>
    /// A client: follows the host's world clock, <paramref name="worldTime"/> at network time
    /// <paramref name="serverTime"/>, from now on.
    /// </summary>
    public void Anchor(double worldTime, double serverTime)
    {
        anchorWorldTime = worldTime;
        anchorServerTime = serverTime;
        IsAnchored = true;
    }
}
