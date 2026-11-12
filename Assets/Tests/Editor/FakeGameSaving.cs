/// <summary>
/// <see cref="IGameSaving"/> whose answers tests set directly, counting the saves asked for.
/// </summary>
public sealed class FakeGameSaving : IGameSaving
{
    public bool CanSave { get; set; } = true;
    public bool LastWriteFailed { get; set; }
    public bool Succeeds { get; set; } = true;
    public int Saves { get; private set; }

    /// <summary>
    /// Counts the save and answers <see cref="Succeeds"/>, recording a failure as the last write's.
    /// </summary>
    public bool Save()
    {
        Saves++;
        LastWriteFailed = !Succeeds;
        return Succeeds;
    }
}
