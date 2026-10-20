using System.Collections.Generic;

/// <summary>
/// An <see cref="IClientReadiness"/> for tests: every client is ready for every chunk unless the test lists it as not
/// ready; trips begun are recorded.
/// </summary>
public sealed class FakeClientReadiness : IClientReadiness
{
    public HashSet<ChunkId> NotReady { get; } = new();
    public List<(ulong Client, int Trip)> Trips { get; } = new();

    /// <summary>
    /// Always true.
    /// </summary>
    public bool IsClientReady(ulong clientId)
    {
        return true;
    }

    /// <summary>
    /// True unless <paramref name="chunk"/> is listed as not ready.
    /// </summary>
    public bool IsClientReady(ulong clientId, ChunkId chunk)
    {
        return !NotReady.Contains(chunk);
    }

    /// <summary>
    /// Records the trip.
    /// </summary>
    public void BeginTrip(ulong clientId, int trip)
    {
        Trips.Add((clientId, trip));
    }
}
