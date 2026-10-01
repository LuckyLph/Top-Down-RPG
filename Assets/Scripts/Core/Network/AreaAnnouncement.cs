using Unity.Netcode;

public readonly struct AreaAnnouncement
{
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

    public void Write(FastBufferWriter writer)
    {
        writer.WriteValueSafe(ScenePath);
        writer.WriteValueSafe(SpawnId);
        writer.WriteValueSafe(Epoch);
        writer.WriteValueSafe(NewSession);
    }

    public static AreaAnnouncement Read(FastBufferReader reader)
    {
        reader.ReadValueSafe(out string scenePath);
        reader.ReadValueSafe(out string spawnId);
        reader.ReadValueSafe(out int epoch);
        reader.ReadValueSafe(out bool newSession);
        return new AreaAnnouncement(scenePath, spawnId, epoch, newSession);
    }
}
