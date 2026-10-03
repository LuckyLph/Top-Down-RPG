public sealed class FakeSessionStatus : ISessionStatus
{
    public bool IsActive { get; set; }
    public bool IsHost { get; set; }
}
