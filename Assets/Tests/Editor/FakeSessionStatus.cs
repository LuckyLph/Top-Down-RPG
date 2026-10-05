/// <summary>
/// <see cref="ISessionStatus"/> whose active and host flags tests set directly instead of asking Netcode.
/// </summary>
public sealed class FakeSessionStatus : ISessionStatus
{
    public bool IsActive { get; set; }
    public bool IsHost { get; set; }
}
