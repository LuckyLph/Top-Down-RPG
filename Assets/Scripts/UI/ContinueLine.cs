using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// The line under Continue and Host and continue (Docs/UiRevamp.md, Phase 9): where the latest save stands, how long it
/// was played and when it was saved, such as "Clearing · 3 h 12 min · today 14:02". The place is the display name of
/// the entry nearest the local player's saved place (its world's name when the world has no named entry); the time
/// played is the session's game time; the save's time is today's, yesterday's, a date this year or a date with its
/// year, on a 24-hour clock in this machine's time zone. Menu-scope singleton, on this machine only.
/// </summary>
public sealed class ContinueLine
{
    public const string Separator = " · ";
    public const string UnderAMinute = "under a minute";

    private readonly GameSave save;
    private readonly GameScenes scenes;
    private readonly UserSettings settings;
    private readonly IWallClock wallClock;

    /// <summary>
    /// Describes <paramref name="save"/>'s latest save, its places read from <paramref name="scenes"/>' worlds and the
    /// local player found by <paramref name="settings"/>' player id, dated against <paramref name="wallClock"/>.
    /// </summary>
    public ContinueLine(GameSave save, GameScenes scenes, UserSettings settings, IWallClock wallClock)
    {
        this.save = save;
        this.scenes = scenes;
        this.settings = settings;
        this.wallClock = wallClock;
    }

    /// <summary>
    /// The line of the save Continue loads; false when there is none.
    /// </summary>
    public bool TryGet(out string line)
    {
        line = string.Empty;
        if (!save.TryLoadLatest(out GameSaveData data))
        {
            return false;
        }

        line = Format(PlaceOf(data, settings.PlayerId, scenes), data.Time, data.SavedAtUtc().ToLocalTime(), wallClock.UtcNow.ToLocalTime());
        return true;
    }

    /// <summary>
    /// The line for a save at <paramref name="place"/> (left out when empty), played for <paramref name="seconds"/> of
    /// game time and saved at <paramref name="savedLocal"/>, read at <paramref name="nowLocal"/>.
    /// </summary>
    public static string Format(string place, double seconds, DateTime savedLocal, DateTime nowLocal)
    {
        string rest = Played(seconds) + Separator + Saved(savedLocal, nowLocal);
        return string.IsNullOrEmpty(place) ? rest : place + Separator + rest;
    }

    /// <summary>
    /// <paramref name="seconds"/> as hours and minutes: "3 h 12 min", "3 h", "12 min", or "under a minute".
    /// </summary>
    public static string Played(double seconds)
    {
        if (seconds < 60d)
        {
            return UnderAMinute;
        }

        long minutes = (long)Math.Floor(seconds / 60d);
        long hours = minutes / 60;
        long rest = minutes % 60;
        if (hours == 0)
        {
            return rest.ToString(CultureInfo.InvariantCulture) + " min";
        }

        string h = hours.ToString(CultureInfo.InvariantCulture) + " h";
        return rest == 0 ? h : h + " " + rest.ToString(CultureInfo.InvariantCulture) + " min";
    }

    /// <summary>
    /// When <paramref name="savedLocal"/> was, read at <paramref name="nowLocal"/>: "today 14:02", "yesterday 14:02",
    /// "10 Nov 14:02" this year, or "10 Nov 2025" before it.
    /// </summary>
    public static string Saved(DateTime savedLocal, DateTime nowLocal)
    {
        string time = savedLocal.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (savedLocal.Date == nowLocal.Date)
        {
            return "today " + time;
        }

        if (savedLocal.Date == nowLocal.Date.AddDays(-1))
        {
            return "yesterday " + time;
        }

        string day = savedLocal.ToString("d MMM", CultureInfo.InvariantCulture);
        return savedLocal.Year == nowLocal.Year ? day + " " + time : day + " " + savedLocal.Year.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The display name of the entry nearest the saved place of <paramref name="playerId"/> (the save's first player
    /// when it does not hold that one), else its world's display name; empty when the world is not one
    /// <paramref name="worlds"/> lists.
    /// </summary>
    public static string PlaceOf(GameSaveData data, string playerId, GameScenes worlds)
    {
        GameSaveData.Player player = null;
        foreach (GameSaveData.Player candidate in data.Players)
        {
            if (candidate != null && (player == null || candidate.PlayerId == playerId))
            {
                player = candidate;
                if (candidate.PlayerId == playerId)
                {
                    break;
                }
            }
        }

        WorldDefinition world = player != null && worlds != null ? worlds.FindWorldById(player.WorldId) : null;
        if (world == null)
        {
            return string.Empty;
        }

        string nearest = string.Empty;
        float best = float.MaxValue;
        foreach (WorldDefinition.Entry entry in world.Entries)
        {
            float distance = entry != null ? Vector2.SqrMagnitude(world.ToWorld(entry.Position) - player.Position) : float.MaxValue;
            if (entry != null && entry.DisplayName.Length > 0 && distance < best)
            {
                best = distance;
                nearest = entry.DisplayName;
            }
        }

        return nearest.Length > 0 ? nearest : world.DisplayName;
    }
}
