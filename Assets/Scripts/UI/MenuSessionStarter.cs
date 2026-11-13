using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine;

/// <summary>
/// Starts sessions from the main menu and Play together (Docs/UiRevamp.md, Phase 9), the logic the main menu's entry
/// points used to hold. Every session runs in seamless worlds (Docs/WorldStreaming.md) with a save file of its own
/// (Docs/KnownGaps.md, Phase 5):
/// <list type="bullet">
/// <item>a new game starts a new session at the starting world's entry;</item>
/// <item>Continue, and hosting to continue, load the latest save (<see cref="GameSave.TryLoadLatest"/>), the local
/// player starting at its saved place (<see cref="PlayerStarts"/>);</item>
/// <item>the Saved games screen plays or hosts a given save (<see cref="Play"/>, <see cref="Host(string)"/>;
/// Docs/UiRevamp.md, Phase 10), a joined client's copy going on as a session of its own;</item>
/// <item>hosting a new game starts a new session hosted;</item>
/// <item>joining connects to a host, remembers it among the recent hosts, shows "Joining …'s game…" over the fader and
/// starts the joined game in whichever world the host puts this player.</item>
/// </list>
/// Nothing starts while a transition runs or a join is in progress. Each failure comes back as the sentence to show,
/// saying what happened and what to do; a start that went ahead returns null.
/// </summary>
public sealed class MenuSessionStarter
{
    public const string NoWorld = "There is no world to start.";
    public const string NoSave = "No saved game to continue.";
    public const string UnreadableSave = "That saved game could not be read.";
    public const string HostFailedFormat = "Could not host on port {0}. Close any other copy of the game and try again.";
    public const string UnreachableFormat = "Could not reach {0} in {1} s. Check the address, and that the host is in a game.";
    public const string FullFormat = "{0} is full: {1} of {1} players.";
    public const string OtherBuildFormat = "The host runs {0} and you run {1}. Play together needs the same build.";
    public const string RefusedFormat = "{0} did not let you in.";
    public const string JoiningFormat = "Joining {0}'s game…";

    private readonly IGameFlow gameFlow;
    private readonly ISessionConnection session;
    private readonly GameSave gameSave;
    private readonly SessionSave sessionSave;
    private readonly PlayerStarts playerStarts;
    private readonly UserSettings settings;
    private readonly LoadingMessage loading;

    /// <summary>
    /// Starts sessions through <paramref name="gameFlow"/> and <paramref name="session"/>, from
    /// <paramref name="gameSave"/>'s saves into <paramref name="sessionSave"/>, at <paramref name="playerStarts"/>' places,
    /// keeping recent hosts in <paramref name="settings"/> and the joining line in <paramref name="loading"/>.
    /// </summary>
    public MenuSessionStarter(IGameFlow gameFlow, ISessionConnection session, GameSave gameSave, SessionSave sessionSave, PlayerStarts playerStarts, UserSettings settings, LoadingMessage loading)
    {
        this.gameFlow = gameFlow;
        this.session = session;
        this.gameSave = gameSave;
        this.sessionSave = sessionSave;
        this.playerStarts = playerStarts;
        this.settings = settings;
        this.loading = loading;
    }

    public bool IsJoining { get; private set; }

    /// <summary>
    /// Whether a start would go ahead now: no join in progress and no transition running.
    /// </summary>
    public bool CanStart => !IsJoining && !gameFlow.IsTransitioning;

    /// <summary>
    /// Whether there is a save to continue.
    /// </summary>
    public bool HasSave => gameSave.HasSave;

    /// <summary>
    /// Every save, newest first (<see cref="GameSave.List"/>).
    /// </summary>
    public IReadOnlyList<SavedGame> SavedGames => gameSave.List();

    /// <summary>
    /// The port a join goes to when the address names none.
    /// </summary>
    public ushort DefaultPort => session.Settings.Port;

    /// <summary>
    /// The address the main menu's JoinGame entry point joins: a host on this machine.
    /// </summary>
    public string DefaultAddress => session.Settings.DefaultAddress;

    /// <summary>
    /// How long a join waits for the host, in seconds.
    /// </summary>
    public float ConnectTimeoutSeconds => session.Settings.ConnectTimeoutSeconds;

    /// <summary>
    /// Starts a new offline session at the starting world's entry; null when it started, else why not.
    /// </summary>
    public string StartNew()
    {
        if (!CanStart)
        {
            return null;
        }

        sessionSave.StartNew();
        return StartLocalPlayerSession();
    }

    /// <summary>
    /// Continues the latest save offline, the local player at its saved place; null when it started, else why not.
    /// </summary>
    public string Continue()
    {
        if (!CanStart)
        {
            return null;
        }

        if (!gameSave.TryLoadLatest(out GameSaveData data))
        {
            return NoSave;
        }

        sessionSave.StartFrom(data);
        return StartLocalPlayerSession();
    }

    /// <summary>
    /// Plays the save in the file <paramref name="name"/> offline, the local player at its saved place; null when it
    /// started, else why not.
    /// </summary>
    public string Play(string name)
    {
        if (!CanStart)
        {
            return null;
        }

        if (!gameSave.TryLoad(name, out GameSaveData data))
        {
            return UnreadableSave;
        }

        sessionSave.StartFrom(data);
        return StartLocalPlayerSession();
    }

    /// <summary>
    /// Starts hosting, then continues the latest save when <paramref name="continueLatest"/> and there is one, or else
    /// starts a new session; null when it started, else why not, logging an error when hosting failed.
    /// </summary>
    public string Host(bool continueLatest)
    {
        if (!CanStart)
        {
            return null;
        }

        GameSaveData data = null;
        if (continueLatest)
        {
            gameSave.TryLoadLatest(out data);
        }

        return StartHosting(data);
    }

    /// <summary>
    /// Starts hosting the save in the file <paramref name="name"/>, the local player at its saved place; null when it
    /// started, else why not, logging an error when hosting failed.
    /// </summary>
    public string Host(string name)
    {
        if (!CanStart)
        {
            return null;
        }

        return gameSave.TryLoad(name, out GameSaveData data) ? StartHosting(data) : UnreadableSave;
    }

    /// <summary>
    /// Starts hosting, then the session of <paramref name="data"/>, or a new one for null; null when it started, else
    /// why not, logging an error when hosting failed.
    /// </summary>
    private string StartHosting(GameSaveData data)
    {
        if (gameFlow.Scenes == null || gameFlow.Scenes.StartingWorld == null)
        {
            Debug.LogError($"{nameof(GameScenes)} has no starting world; hosting needs one.");
            return NoWorld;
        }

        if (!session.StartHost())
        {
            Debug.LogError($"Could not start hosting on port {session.Settings.Port}.");
            return string.Format(CultureInfo.InvariantCulture, HostFailedFormat, session.Settings.Port);
        }

        if (data != null)
        {
            sessionSave.StartFrom(data);
        }
        else
        {
            sessionSave.StartNew();
        }

        return StartLocalPlayerSession();
    }

    /// <summary>
    /// Connects to <paramref name="address"/> within the connect timeout, blocking other starts meanwhile, then keeps it
    /// first among the recent hosts, shows the joining line and starts the joined game; null when it joined or was
    /// cancelled through <paramref name="cancellation"/>, else why not: refused (full, another build, or otherwise) or
    /// unreachable.
    /// </summary>
    public async Awaitable<string> JoinAsync(JoinAddress address, CancellationToken cancellation)
    {
        if (!CanStart)
        {
            return null;
        }

        IsJoining = true;
        bool connected;
        try
        {
            connected = await session.JoinAsync(address.Host, address.Port, session.Settings.ConnectTimeoutSeconds, cancellation);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            IsJoining = false;
        }

        if (!connected)
        {
            return JoinFailure(address, session.JoinRefusal);
        }

        settings.AddRecentHost(address.ToShortString(session.Settings.Port));
        sessionSave.StartNew();
        loading.Show(string.Format(CultureInfo.InvariantCulture, JoiningFormat, address.Host));
        _ = gameFlow.StartJoinedGameAsync();
        return null;
    }

    /// <summary>
    /// What to say when a join to <paramref name="address"/> failed with <paramref name="refusal"/> (null when the host
    /// never answered).
    /// </summary>
    public string JoinFailure(JoinAddress address, string refusal)
    {
        if (JoinApproval.IsFull(refusal, out int maxPlayers))
        {
            return string.Format(CultureInfo.InvariantCulture, FullFormat, address, maxPlayers);
        }

        if (JoinApproval.IsOtherBuild(refusal, out string hostVersion))
        {
            return string.Format(CultureInfo.InvariantCulture, OtherBuildFormat, hostVersion, Application.version);
        }

        if (refusal != null)
        {
            return string.Format(CultureInfo.InvariantCulture, RefusedFormat, address);
        }

        return string.Format(CultureInfo.InvariantCulture, UnreachableFormat, address, Mathf.RoundToInt(session.Settings.ConnectTimeoutSeconds));
    }

    /// <summary>
    /// Starts a world session with the local player where <see cref="PlayerStarts"/> puts this install's player id;
    /// null when it started, else why not.
    /// </summary>
    private string StartLocalPlayerSession()
    {
        if (!playerStarts.TryGetStart(session.LocalPlayerId, out WorldDefinition world, out Vector2 position))
        {
            return NoWorld;
        }

        _ = gameFlow.StartWorldGameAsync(world, position);
        return null;
    }
}
