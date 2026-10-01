public sealed class MemorySaveStore : ISaveStore
{
    public string Contents { get; set; }
    public int WriteCount { get; private set; }

    public bool TryRead(out string contents)
    {
        contents = Contents;
        return contents != null;
    }

    public bool Write(string contents)
    {
        Contents = contents;
        WriteCount++;
        return true;
    }
}
