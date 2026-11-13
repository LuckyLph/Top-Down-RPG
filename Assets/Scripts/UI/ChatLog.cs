using System;
using System.Globalization;

/// <summary>
/// The session's chat lines on this machine (Docs/UiRevamp.md, Phase 12), oldest first: players' messages as the host
/// relays them (<see cref="PlayerChat"/>) and the party's lines this machine raises itself (<see cref="ChatPartyLines"/>).
/// It keeps the newest <see cref="Capacity"/>, the oldest dropping off, and nothing is saved: a late joiner sees only
/// the lines sent after it joined. Each line is stamped with the game time it arrived, which its fading counts from, and
/// the local time of day, "14:32", built once as it arrives.
/// </summary>
public sealed class ChatLog
{
    public const int Capacity = 32;

    private readonly IClock clock;
    private readonly IWallClock wallClock;
    private readonly ChatLine[] lines = new ChatLine[Capacity];
    private int first;

    /// <summary>
    /// An empty log stamping its lines with <paramref name="gameClock"/> and <paramref name="wall"/>.
    /// </summary>
    public ChatLog(IClock gameClock, IWallClock wall)
    {
        clock = gameClock;
        wallClock = wall;
    }

    /// <summary>
    /// Raised with each line as it is added.
    /// </summary>
    public event Action<ChatLine> Added;

    /// <summary>
    /// Raised when the log is emptied.
    /// </summary>
    public event Action Cleared;

    /// <summary>
    /// How many lines the log holds, up to <see cref="Capacity"/>.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// The line at <paramref name="index"/>, the oldest at 0.
    /// </summary>
    public ChatLine this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return lines[(first + index) % Capacity];
        }
    }

    /// <summary>
    /// Adds the message <paramref name="text"/> from the player in <paramref name="slot"/>; ignores a slot that is none
    /// and an empty text.
    /// </summary>
    public void AddPlayerLine(int slot, string text)
    {
        if (PlayerSlots.IsSlot(slot))
        {
            Add(slot, text);
        }
    }

    /// <summary>
    /// Adds the line about the party <paramref name="text"/>; ignores an empty text.
    /// </summary>
    public void AddPartyLine(string text)
    {
        Add(0, text);
    }

    /// <summary>
    /// Forgets every line.
    /// </summary>
    public void Clear()
    {
        Array.Clear(lines, 0, lines.Length);
        first = 0;
        Count = 0;
        Cleared?.Invoke();
    }

    /// <summary>
    /// Adds a line of <paramref name="text"/> for <paramref name="slot"/> stamped now, dropping the oldest when full.
    /// </summary>
    private void Add(int slot, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        string stamp = wallClock != null ? wallClock.UtcNow.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) : string.Empty;
        ChatLine line = new(slot, text, clock != null ? clock.Time : 0f, stamp);
        if (Count < Capacity)
        {
            lines[(first + Count) % Capacity] = line;
            Count++;
        }
        else
        {
            lines[first] = line;
            first = (first + 1) % Capacity;
        }

        Added?.Invoke(line);
    }
}
