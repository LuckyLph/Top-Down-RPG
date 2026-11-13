using System.Collections.Generic;

/// <summary>
/// In-memory <see cref="ISaveFolder"/> a benchmark run is given (<see cref="StreamingBenchmarkRunner"/>), so it reads no
/// save and writes none: it starts empty, with no earlier build's save file, and keeps only what the run writes.
/// </summary>
public sealed class BenchmarkSaveFolder : ISaveFolder
{
    private readonly Dictionary<string, BenchmarkSaveStore> files = new();

    public ISaveStore Legacy { get; } = new BenchmarkSaveStore();

    /// <summary>
    /// The names of the files the run opened.
    /// </summary>
    public IReadOnlyList<string> Names()
    {
        return new List<string>(files.Keys);
    }

    /// <summary>
    /// The in-memory store of <paramref name="name"/>, made empty on first use.
    /// </summary>
    public ISaveStore Open(string name)
    {
        if (!files.TryGetValue(name, out BenchmarkSaveStore store))
        {
            store = new BenchmarkSaveStore();
            files.Add(name, store);
        }

        return store;
    }

    /// <summary>
    /// Forgets the file <paramref name="name"/> the run wrote, keeping no copy; false when the run never opened it.
    /// </summary>
    public bool Delete(string name)
    {
        return files.Remove(name);
    }

    /// <summary>
    /// Nothing to put away: the run has no earlier build's save file. Always succeeds.
    /// </summary>
    public bool RetireLegacy(out string moved)
    {
        moved = null;
        return true;
    }
}
