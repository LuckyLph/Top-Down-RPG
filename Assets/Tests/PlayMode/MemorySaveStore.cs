/// <summary>
/// In-memory <see cref="ISaveStore"/> for tests that keeps the last written contents and counts the writes.
/// </summary>
public sealed class MemorySaveStore : ISaveStore
{
    public string Contents { get; set; }
    public int WriteCount { get; private set; }

    /// <summary>
    /// Returns the stored contents; false while nothing has been written or set.
    /// </summary>
    public bool TryRead(out string contents)
    {
        contents = Contents;
        return contents != null;
    }

    /// <summary>
    /// Replaces the stored contents, counts the write and always succeeds.
    /// </summary>
    public bool Write(string contents)
    {
        Contents = contents;
        WriteCount++;
        return true;
    }
}
