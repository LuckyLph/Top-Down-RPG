public sealed class FakeSessionStatus : ISessionStatus
{
    public bool IsOnline { get; set; }
    public bool IsHost { get; set; }
}
