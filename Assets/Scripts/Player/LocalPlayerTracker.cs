using System;

/// <summary>
/// Holds the <see cref="LocalPlayer"/> this machine controls, if any, and raises <see cref="Changed"/> whenever it is
/// assigned or cleared so the camera and HUD can rebind.
/// </summary>
public sealed class LocalPlayerTracker
{
    public event Action<LocalPlayer> Changed;

    public LocalPlayer Current { get; private set; }

    /// <summary>
    /// Makes <paramref name="localPlayer"/> (null for none) the current local player, raising <see cref="Changed"/>
    /// only when it differs from the current one.
    /// </summary>
    public void Assign(LocalPlayer localPlayer)
    {
        if (Current == localPlayer)
        {
            return;
        }

        Current = localPlayer;
        Changed?.Invoke(Current);
    }

    /// <summary>
    /// Clears the current local player if it is the one behind <paramref name="handle"/>; other handles are ignored.
    /// </summary>
    public void Clear(PlayerHandle handle)
    {
        if (Current != null && Current.Handle == handle)
        {
            Assign(null);
        }
    }
}
