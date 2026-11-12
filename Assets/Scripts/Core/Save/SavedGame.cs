using System;

/// <summary>
/// One save file as <see cref="GameSave.List"/> finds it (Docs/UiRevamp.md, Phase 10): its file's name, which
/// <see cref="GameSave.TryLoad"/> and <see cref="GameSave.Delete"/> take, and what it holds: a session's save or a
/// joined client's copy of its place in a host's session, which plays on its own as a session of its own.
/// </summary>
public sealed class SavedGame
{
    /// <summary>
    /// The save file <paramref name="name"/>, holding <paramref name="data"/>.
    /// </summary>
    public SavedGame(string name, GameSaveData data)
    {
        Name = name;
        Data = data;
    }

    public string Name { get; }
    public GameSaveData Data { get; }
    public string Id => Data.Id;
    public bool IsCopy => Data.IsCopy;
    public bool HostGone => Data.HostGone;
    public double Time => Data.Time;

    /// <summary>
    /// When it was saved, in UTC.
    /// </summary>
    public DateTime SavedAtUtc()
    {
        return Data.SavedAtUtc();
    }
}
