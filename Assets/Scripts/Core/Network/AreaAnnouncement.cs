using Unity.Netcode;

/// <summary>
/// The host's message telling clients which area to load: its scene path, the spawn point, the area epoch clients
/// echo back when they report ready, and whether it starts a new session rather than changing area.
/// </summary>
public readonly struct AreaAnnouncement
{
    /// <summary>
    /// Creates an announcement, storing null <paramref name="scenePath"/> or <paramref name="spawnId"/> as empty
    /// strings so it always serializes.
    /// </summary>
    public AreaAnnouncement(string scenePath, string spawnId, int epoch, bool newSession)
    {
        ScenePath = scenePath ?? string.Empty;
        SpawnId = spawnId ?? string.Empty;
        Epoch = epoch;
        NewSession = newSession;
    }

    public string ScenePath { get; }
    public string SpawnId { get; }
    public int Epoch { get; }
    public bool NewSession { get; }

    /// <summary>
    /// Writes every field to <paramref name="writer"/> in the order <see cref="Read"/> expects.
    /// </summary>
    public void Write(FastBufferWriter writer)
    {
        writer.WriteValueSafe(ScenePath);
        writer.WriteValueSafe(SpawnId);
        writer.WriteValueSafe(Epoch);
        writer.WriteValueSafe(NewSession);
    }

    /// <summary>
    /// Reads an announcement written by <see cref="Write"/> from <paramref name="reader"/>.
    /// </summary>
    public static AreaAnnouncement Read(FastBufferReader reader)
    {
        reader.ReadValueSafe(out string scenePath);
        reader.ReadValueSafe(out string spawnId);
        reader.ReadValueSafe(out int epoch);
        reader.ReadValueSafe(out bool newSession);
        return new AreaAnnouncement(scenePath, spawnId, epoch, newSession);
    }
}
