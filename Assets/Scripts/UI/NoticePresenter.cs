using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point for the HUD's notice line (Docs/UiRevamp.md, Phase 3), on this machine only. Notices
/// queue: each shows for <see cref="HudSettings.NoticeSeconds"/> of game time, then the next one; a notice raised again
/// while it shows restarts its time, and one already waiting is not queued twice. Their time stands still while
/// <see cref="GameFlow"/> runs a transition, behind the fade. It raises, from state this machine already has:
/// <list type="bullet">
/// <item>"Waystone discovered" (<see cref="GameplaySettings.WaystoneDiscoveredText"/>) when the local player discovers
/// a waystone (<see cref="WaystoneNotices"/>);</item>
/// <item>"Entering the Overworld" when the local player is put in another world (<see cref="PlayerWorlds"/>): at the
/// start of a session, on joining and after a trip to another world, from the world's display name;</item>
/// <item>while the local player is dead, "You fell. Respawning in 3", counting whole seconds down from
/// <see cref="GameplaySettings.RespawnDelaySeconds"/> since it died here, then "Respawning..." until it is restored. It
/// holds the line, and the queue waits behind it.</item>
/// </list>
/// Other systems raise notices with <see cref="Show"/>. Every text it shows itself is built once, so
/// <see cref="Tick"/> allocates nothing.
/// </summary>
public sealed class NoticePresenter : IStartable, ITickable, IDisposable
{
    public const string EnteringFormat = "Entering the {0}";
    public const string FellFormat = "You fell. Respawning in {0}";
    public const string Respawning = "Respawning...";

    private readonly NoticeView view;
    private readonly HudSettings hudSettings;
    private readonly GameplaySettings settings;
    private readonly IClock clock;
    private readonly LocalPlayerTracker localPlayer;
    private readonly PlayerWorlds playerWorlds;
    private readonly WaystoneNotices waystones;
    private readonly GameFlow gameFlow;
    private readonly Queue<string> waiting = new(8);
    private readonly Dictionary<WorldDefinition, string> entering = new();
    private string[] countdown = new string[0];
    private string current;
    private float shownFor;
    private float lastTime;
    private Health health;
    private bool dead;
    private float diedAt = float.NaN;
    private WorldDefinition announced;
    private bool started;

    /// <summary>
    /// Captures the line it shows notices on, the notice time, the respawn delay and waystone text, the game clock,
    /// the local player and its world, the waystone discoveries and the flow whose transitions hold the time.
    /// </summary>
    public NoticePresenter(
        NoticeView view,
        HudSettings hudSettings,
        GameplaySettings settings,
        IClock clock,
        LocalPlayerTracker localPlayer,
        PlayerWorlds playerWorlds,
        WaystoneNotices waystones,
        GameFlow gameFlow)
    {
        this.view = view;
        this.hudSettings = hudSettings;
        this.settings = settings;
        this.clock = clock;
        this.localPlayer = localPlayer;
        this.playerWorlds = playerWorlds;
        this.waystones = waystones;
        this.gameFlow = gameFlow;
    }

    /// <summary>
    /// The notice the line shows: the death notice while the local player is dead, else the current one; null when
    /// none shows.
    /// </summary>
    public string Shown => Held ?? current;

    /// <summary>
    /// How many notices wait behind the one that shows, for tests.
    /// </summary>
    internal int WaitingCount => waiting.Count;

    /// <summary>
    /// Whether <paramref name="notice"/> shows or waits, for tests.
    /// </summary>
    internal bool Holds(string notice)
    {
        return notice != null && (Shown == notice || current == notice || waiting.Contains(notice));
    }

    /// <summary>
    /// Builds the countdown's texts, listens to the local player, its world and its discoveries, and binds the local
    /// player there is.
    /// </summary>
    public void Start()
    {
        int seconds = Mathf.Max(0, Mathf.CeilToInt(settings.RespawnDelaySeconds));
        countdown = new string[seconds + 1];
        for (int second = 1; second <= seconds; second++)
        {
            countdown[second] = string.Format(FellFormat, second);
        }

        lastTime = clock.Time;
        localPlayer.Changed += Bind;
        playerWorlds.Changed += HandleWorldChanged;
        waystones.Discovered += HandleDiscovered;
        started = true;
        Bind(localPlayer.Current);
        Refresh();
    }

    /// <summary>
    /// Stops listening and lets go of the local player's health.
    /// </summary>
    public void Dispose()
    {
        if (!started)
        {
            return;
        }

        started = false;
        localPlayer.Changed -= Bind;
        playerWorlds.Changed -= HandleWorldChanged;
        waystones.Discovered -= HandleDiscovered;
        Unbind();
    }

    /// <summary>
    /// Queues <paramref name="notice"/>: it shows at once when nothing does, restarts its time when it already shows,
    /// and waits behind the others otherwise, once. Ignores an empty notice.
    /// </summary>
    public void Show(string notice)
    {
        if (string.IsNullOrEmpty(notice))
        {
            return;
        }

        if (current == notice)
        {
            shownFor = 0f;
        }
        else if (current == null)
        {
            current = notice;
            shownFor = 0f;
        }
        else if (!waiting.Contains(notice))
        {
            waiting.Enqueue(notice);
        }

        Refresh();
    }

    /// <summary>
    /// Runs the shown notice's time, unless the death notice holds the line or a transition runs, moves on to the next
    /// when it is up, and shows what the line holds now, the countdown's second included.
    /// </summary>
    public void Tick()
    {
        float now = clock.Time;
        float elapsed = Mathf.Max(0f, now - lastTime);
        lastTime = now;
        if (!dead && current != null && (gameFlow == null || !gameFlow.IsTransitioning))
        {
            shownFor += elapsed;
            if (shownFor >= hudSettings.NoticeSeconds)
            {
                current = waiting.Count > 0 ? waiting.Dequeue() : null;
                shownFor = 0f;
            }
        }

        Refresh();
    }

    /// <summary>
    /// The death notice while the local player is dead: the countdown's whole seconds left of the respawn delay since
    /// it died here, then "Respawning..." (at once for a player found dead, whose death this machine did not see).
    /// </summary>
    private string Held
    {
        get
        {
            if (!dead)
            {
                return null;
            }

            if (float.IsNaN(diedAt))
            {
                return Respawning;
            }

            int second = Mathf.CeilToInt(settings.RespawnDelaySeconds - (clock.Time - diedAt) - 0.0001f);
            return second >= 1 && second < countdown.Length ? countdown[second] : Respawning;
        }
    }

    /// <summary>
    /// Shows the line's notice, or hides it.
    /// </summary>
    private void Refresh()
    {
        if (view == null)
        {
            return;
        }

        string notice = Shown;
        if (notice == null)
        {
            view.Hide();
        }
        else
        {
            view.Show(notice);
        }
    }

    /// <summary>
    /// Follows <paramref name="player"/>'s death and restore, holding "Respawning..." when it is already dead, and
    /// announces the world it is in.
    /// </summary>
    private void Bind(LocalPlayer player)
    {
        Unbind();
        health = player != null ? player.Handle.Health : null;
        if (health != null)
        {
            health.Died += HandleDied;
            health.Restored += HandleRestored;
            dead = health.IsDead;
            diedAt = float.NaN;
        }

        if (player != null)
        {
            Announce(playerWorlds.Get(player.Handle));
        }

        Refresh();
    }

    /// <summary>
    /// Stops following the bound player's health and drops its death notice.
    /// </summary>
    private void Unbind()
    {
        if (health != null)
        {
            health.Died -= HandleDied;
            health.Restored -= HandleRestored;
        }

        health = null;
        dead = false;
        diedAt = float.NaN;
    }

    /// <summary>
    /// The local player died here: the countdown starts now.
    /// </summary>
    private void HandleDied(Health _)
    {
        dead = true;
        diedAt = clock.Time;
        Refresh();
    }

    /// <summary>
    /// The local player stands again: the death notice goes and the queue goes on.
    /// </summary>
    private void HandleRestored(Health _)
    {
        dead = false;
        diedAt = float.NaN;
        Refresh();
    }

    /// <summary>
    /// Announces the local player's new world.
    /// </summary>
    private void HandleWorldChanged(PlayerHandle player, WorldDefinition world)
    {
        LocalPlayer local = localPlayer.Current;
        if (local != null && player == local.Handle)
        {
            Announce(world);
        }
    }

    /// <summary>
    /// Shows "Entering the" and <paramref name="world"/>'s display name (its asset's name without one) when it is not
    /// the world announced last; a trip within one world, a respawn's included, announces nothing.
    /// </summary>
    internal void Announce(WorldDefinition world)
    {
        if (world == null || world == announced)
        {
            return;
        }

        announced = world;
        if (!entering.TryGetValue(world, out string notice))
        {
            notice = string.Format(EnteringFormat, world.DisplayName.Length > 0 ? world.DisplayName : world.name);
            entering[world] = notice;
        }

        Show(notice);
    }

    /// <summary>
    /// Shows the waystone notice when the local player is the one that discovered a waystone.
    /// </summary>
    private void HandleDiscovered(PlayerHandle player, string entryName)
    {
        LocalPlayer local = localPlayer.Current;
        if (local != null && player == local.Handle)
        {
            Show(settings.WaystoneDiscoveredText);
        }
    }
}
