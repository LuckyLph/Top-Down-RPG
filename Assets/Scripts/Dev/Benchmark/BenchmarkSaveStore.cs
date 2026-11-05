/// <summary>
/// In-memory <see cref="ISaveStore"/> a benchmark run is given for its settings and its save files
/// (<see cref="StreamingBenchmarkRunner"/>, <see cref="BenchmarkSaveFolder"/>), so it reads no file and writes none: it
/// starts empty and keeps only what the run writes.
/// </summary>
public sealed class BenchmarkSaveStore : ISaveStore
{
    private string contents;

    /// <summary>
    /// Returns what the run last wrote; false while it has written nothing.
    /// </summary>
    public bool TryRead(out string stored)
    {
        stored = contents;
        return stored != null;
    }

    /// <summary>
    /// Replaces what is kept with <paramref name="written"/> and always succeeds.
    /// </summary>
    public bool Write(string written)
    {
        contents = written;
        return true;
    }

    /// <summary>
    /// Keeps no copy, since nothing outlives the run, and always succeeds.
    /// </summary>
    public bool KeepCopy(out string copy)
    {
        copy = null;
        return true;
    }
}
