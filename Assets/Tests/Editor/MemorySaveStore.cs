/// <summary>
/// <see cref="ISaveStore"/> that keeps the saved text in memory and counts writes, for save and settings tests.
/// </summary>
public sealed class MemorySaveStore : ISaveStore
{
    public string Contents { get; set; }
    public int WriteCount { get; private set; }

    /// <summary>
    /// Returns the stored text; fails when nothing has been stored (null contents).
    /// </summary>
    public bool TryRead(out string contents)
    {
        contents = Contents;
        return contents != null;
    }

    /// <summary>
    /// Replaces the stored text with <paramref name="contents"/>, counts the write and always succeeds.
    /// </summary>
    public bool Write(string contents)
    {
        Contents = contents;
        WriteCount++;
        return true;
    }
}
