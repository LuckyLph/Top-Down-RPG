using System;

public sealed class LocalPlayerTracker
{
    public event Action<LocalPlayer> Changed;

    public LocalPlayer Current { get; private set; }

    public void Assign(LocalPlayer localPlayer)
    {
        if (Current == localPlayer)
        {
            return;
        }

        Current = localPlayer;
        Changed?.Invoke(Current);
    }

    public void Clear(PlayerHandle handle)
    {
        if (Current != null && Current.Handle == handle)
        {
            Assign(null);
        }
    }
}
